using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO POVOAMENTO (`--povoteste`).
///
/// ============================ AS QUATRO PERGUNTAS QUE SO DAQUI SE RESPONDE ============================
///   1. **O RECORTE ESTA LIGADO?** Nenhum inimigo comum nasce hoje -- e ele NAO nasce por causa do
///      tipo, e nao por outra recusa qualquer. A metade de controle e obrigatoria: o MESMO molde
///      nasce assim que o tipo muda. Sem ela, a checagem passaria ate se `NascerNpc` estivesse
///      quebrada.
///   2. **O CIDADAO E PACIFICO?** Nao basta ler o `AIAlwaysActive = 0` num comentario: mede-se a
///      DISTANCIA ate um alvo ao longo de centenas de tiques. Hostil fecha; habitante nao. E o
///      controle e um chefe no mesmo cenario, que fecha.
///   3. **A RACA VEM DO BERCO?** Nenhum Icer em Vegeta, nenhum Arlian em Namek -- as duas
///      divergencias que a tabela do MENU tem e o berco nao.
///   4. **O TETO DISPARA?** Corolario 0.7. A bancada aperta o teto contra o codigo de PRODUCAO
///      (parametro com padrao, como o `teto` do `Bercos.ServeDeBerco`) e conta os recusados.
/// ==================================================================================================
///
/// E a quinta, que e a do DM: **MOB-ZUMBI**. 5400 tiques com a populacao de producao viva, medindo
/// se o tique fica mais caro com o TEMPO -- com o mesmo criterio e o mesmo defeito injetado que a
/// bancada da IA usa, porque um criterio sem dentes fica verde pra sempre.
///
///     Godot --headless -- --server --povoteste
/// </summary>
public partial class GameServer
{
	private bool _povoDeTeste;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo -- so com a flag.</summary>
	private void RodarBancadaDePovoamento(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DO POVOAMENTO =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		var forjados = new List<ServerPlayer>();

		try
		{
			if (_moldes == null || _racas == null)
			{
				Checa("o npcs.json e o races.json carregaram", false);
				return;
			}

			// =====================================================================
			// 1. O PLANO
			// =====================================================================
			Checa("o plano de povoamento carregou", _moldes.Plano.Length > 0, $"{_moldes.Plano.Length} linhas");
			Checa("o plano nao tem contradicao",
				  Povoamento.Problemas(_moldes.Plano, _moldes).Count == 0,
				  string.Join(" | ", Povoamento.Problemas(_moldes.Plano, _moldes)));

			Checa("todo molde do arquivo declara o 'tipo' (sem ele o recorte nao teria como cortar)",
				  _moldes.Todos.All(m => m.TipoDeclarado),
				  string.Join(", ", _moldes.Todos.Where(m => !m.TipoDeclarado).Select(m => m.Id)));

			// =====================================================================
			// 2. A RACA VEM DO BERCO -- e nao da tabela do menu de criacao
			// =====================================================================
			// ============================ ESTA E A DIFERENCA, E ELA E MEDIDA ============================
			// `CharacterDraft.RacasDoPlaneta` (o MENU) poe Icer em Vegeta e Arlian/Makyo em Namek. Se
			// o sorteio de NPC estivesse lendo aquela tabela -- como estava --, um cidadao de Vegeta
			// podia nascer Frost Demon. A checagem compara as duas listas de proposito: ela nao afirma
			// so "o berco funciona", ela afirma "as duas discordam E o povoamento usa a certa".
			// ======================================================================================
			string[] todasAsRacas = [.. _racas.Protos.Keys];

			foreach (string planeta in (string[])["Earth", "Vegeta", "Namek", "Icer", "Arlia", "Makyo_Star"])
			{
				string[] doBerco = Bercos.RacasNascidasEm(planeta, todasAsRacas);
				Checa($"'{planeta}' tem quem nele nasca pelo berco ({string.Join(", ", doBerco)})",
					  doBerco.Length > 0);
				Checa($"...e toda raca dessa lista tem '{planeta}' como planeta natal",
					  doBerco.All(r => Bercos.PlanetaNatal(r) == planeta));
			}

			Checa("o berco de Vegeta NAO inclui Icer (a tabela do menu de criacao inclui)",
				  !Bercos.RacasNascidasEm("Vegeta", todasAsRacas).Contains("Icer")
				  && CharacterDraft.RacasDoPlaneta("Vegeta").Contains("Icer"));
			Checa("...e o de Namek NAO inclui Arlian nem Makyo (a do menu inclui os dois)",
				  !Bercos.RacasNascidasEm("Namek", todasAsRacas).Contains("Arlian")
				  && CharacterDraft.RacasDoPlaneta("Namek").Contains("Arlian"));
			Checa("...e o crivo tira quem so chega na Terra pelo RECUO (androide orfao, e o 'Dog' que "
				+ "e a raca de EXEMPLO do Genetic_Prototype.dm)",
				  !Bercos.RacasNascidasEm("Earth", todasAsRacas).Contains("Dog")
				  && !Bercos.RacasNascidasEm("Earth", todasAsRacas).Contains("Android"));

			// =====================================================================
			// 3. O RECORTE DO DONO -- inimigo comum NAO nasce
			// =====================================================================
			var zonaVazia = ZoneKey.Premade(
				Espaco.PreFeitos().Select(p => p.Nome)
					.FirstOrDefault(n => !_players.Values.Any(
						p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
				?? "Namek");

			Checa($"o interruptor esta DESLIGADO ({Povoamento.MotivoDoInimigoDesligado})",
				  !Povoamento.InimigoComumLigado);

			MoldeDeNpc[] inimigos = [.. _moldes.Todos.Where(m => m.Tipo == TipoDeNpc.Inimigo)];
			Checa("ha moldes de inimigo no arquivo (o caminho existe, so esta desligado)",
				  inimigos.Length > 0, $"{inimigos.Length}");

			ulong lugar = 900_000;
			foreach (MoldeDeNpc m in inimigos)
			{
				ServerPlayer? bicho = NascerNpc(m.Id, zonaVazia, new Vec2(0, 0), lugar++);
				if (bicho != null) forjados.Add(bicho);
				Checa($"'{m.Id}' (inimigo comum) NAO nasce", bicho == null);
			}

			// ============================ O CONTROLE: ERA O TIPO, E NAO OUTRA RECUSA ============================
			// O MESMO molde, a MESMA funcao de producao, o MESMO lugar -- so o tipo muda. Sem esta
			// metade, as quatro linhas de cima passariam com `NascerNpc` devolvendo nulo por qualquer
			// motivo (races.json ausente, molde incoerente, uma excecao engolida).
			// ==============================================================================================
			{
				MoldeDeNpc cobaia = inimigos[0];
				TipoDeNpc antes = cobaia.Tipo;
				cobaia.Tipo = TipoDeNpc.Cidadao;
				ServerPlayer? agora = NascerNpc(cobaia.Id, zonaVazia, new Vec2(0, 0), lugar++);
				if (agora != null) forjados.Add(agora);
				cobaia.Tipo = antes;

				Checa($"...e o MESMO molde ('{cobaia.Id}') nasce assim que o TIPO muda -- era o recorte, "
					+ "e nao outra recusa", agora != null);
			}

			// E O CHEFE DE SAGA NASCE, que e a outra metade do pedido do dono.
			{
				ServerPlayer? chefe = NascerNpc("freeza_vegeta", zonaVazia, new Vec2(256, 0), lugar++);
				if (chefe != null) forjados.Add(chefe);
				Checa("o CHEFE DE SAGA nasce (o dono pediu os dois lados do corte)",
					  chefe != null && chefe.Papel?.Tipo == TipoDeNpc.Chefe);
				Checa("...e o BP dele e o PINADO da ficha, nao um sorteio",
					  chefe != null && Math.Abs(chefe.Ficha.BP - 530_000) < 1, $"{chefe?.Ficha.BP:N0}");
			}

			// =====================================================================
			// 4. O POVOAMENTO DE PRODUCAO -- deixa o mundo nascer inteiro
			// =====================================================================
			// Sem atalho: e o `TickDoPovoamento` do tique, chamado ate a fila secar. Um laco proprio
			// que chamasse `NascerNpc` direto mediria o atalho e nao o jogo (regra 0.7).
			int tiques = 0;
			while (tiques < 20_000
				   && (_filaDoPovoamento.Count > 0 || tiques == 0
					   || CorposSemDonoNoServidor() < _moldes.Plano.Sum(l => l.Quantos)))
			{
				TickDoPovoamento(Protocol.TickSeconds);
				tiques++;
				if (_filaDoPovoamento.Count == 0 && tiques > 1 && !PrecisaDeMais()) break;
			}

			bool PrecisaDeMais() => _moldes!.Plano.Any(
				l => ContarHabitantes(l.Planeta, l.Molde) < l.Quantos);

			GD.Print($"  ---- povoamento completo em {tiques} tiques ({tiques / 30.0:0.0} s de jogo) ----");
			foreach (LinhaDePovoamento l in _moldes.Plano)
			{
				int tem = ContarHabitantes(l.Planeta, l.Molde);
				Checa($"'{l.Planeta}' tem os {l.Quantos} habitantes do plano", tem == l.Quantos, $"{tem}");
			}

			int total = CorposSemDonoNoServidor();
			GD.Print($"  ---- {total} corpos sem dono no servidor ----");

			// AS RACAS QUE DE FATO NASCERAM, por planeta
			foreach (LinhaDePovoamento l in _moldes.Plano)
			{
				string[] permitidas = Bercos.RacasNascidasEm(l.Planeta, todasAsRacas);
				// SO OS HABITANTES DO PLANO. A secao 3 desta bancada forjou um saibaman e um Freeza
				// numa zona pre-feita vazia -- que pode ser justamente uma do plano --, e os dois
				// tem o berco daquele mapa. Contar tudo faria a checagem reprovar por causa das
				// COBAIAS DELA MESMA, que e um falso vermelho e um jeito rapido de ninguem mais
				// acreditar na bancada.
				List<string> nasceram = [.. _players.Values
					.Where(p => p.Papel != null
							 && string.Equals(p.Papel.Molde.Id, l.Molde, StringComparison.OrdinalIgnoreCase)
							 && string.Equals(p.Berco.Natal, l.Planeta, StringComparison.OrdinalIgnoreCase))
					.Select(p => p.Race).Distinct().OrderBy(r => r, StringComparer.Ordinal)];

				GD.Print($"       {l.Planeta}: {string.Join(", ", nasceram)}");
				Checa($"toda raca nascida em '{l.Planeta}' tem berco ali",
					  nasceram.All(permitidas.Contains),
					  string.Join(",", nasceram.Where(r => !permitidas.Contains(r))));
			}

			Checa("nenhum habitante ficou com o nome do molde (a pool por raca esta ligada)",
				  !_players.Values.Any(p => p.Papel?.Tipo == TipoDeNpc.Cidadao && p.Name == "Habitante"));

			// =====================================================================
			// 5. TODO HABITANTE NASCEU COM CEREBRO -- senao ele e estatua
			// =====================================================================
			// A fabrica NAO escrevia este campo, e as duas bancadas antigas o anexavam a mao depois.
			// Ou seja: todo NPC nascido pelo caminho de producao era um boneco parado, e nenhuma
			// bancada podia pegar isso porque nenhuma usava o caminho de producao inteiro.
			int semCerebro = _players.Values.Count(p => p.Papel != null && p.Cerebro == null);
			Checa("todo NPC nasceu com CEREBRO (sem ele o `TickDosCorposSemDono` nao o dirige)",
				  semCerebro == 0, $"{semCerebro} estatuas");

			// E O TEMPERAMENTO CHEGOU: os quatro numeros do molde eram lidos e nunca usados.
			{
				ServerPlayer? um = _players.Values.FirstOrDefault(p => p.Papel?.Tipo == TipoDeNpc.Cidadao);
				MoldeDeNpc cid = _moldes.Get("cidadao")!;
				Checa("o temperamento do molde chegou no cerebro (coragem 80 -> quase nao recua)",
					  um?.Cerebro != null
					  && Math.Abs(um.Cerebro.VidaCautelosa - 0.9 * (1 - cid.Coragem / 100)) < 1e-9,
					  $"{um?.Cerebro?.VidaCautelosa:0.000}");
			}

			// A FASE DA LEITURA DE 1 Hz FOI ESPALHADA. Sem isso, 148 corpos nascidos em 5 s ficariam
			// em fase e a leitura cara aconteceria toda num tique so, uma vez por segundo.
			{
				var faseUm = new Cerebro(); faseUm.DesfasarCapacidades(0.0);
				var faseDois = new Cerebro(); faseDois.DesfasarCapacidades(0.9);
				Checa("`DesfasarCapacidades` muda MESMO o tique da primeira leitura",
					  faseUm.PrecisaLerCapacidades(0.05) && !faseDois.PrecisaLerCapacidades(0.05));
			}

			// =====================================================================
			// 6. O CIDADAO E PACIFICO -- medido, nao lido
			// =====================================================================
			MedirPacifismo(Checa, forjados);

			// =====================================================================
			// 7. O TETO DISPARA
			// =====================================================================
			{
				// APERTA O TETO CONTRA O CODIGO DE PRODUCAO. O parametro existe por isto (regra 0.7):
				// a alternativa seria uma copia da manutencao com outro numero, que testaria a copia.
				_proximaManutencao = 0;                       // forca a manutencao na proxima volta
				int antes = _filaDoPovoamento.Count;
				Manutencao(tetoZona: 1, tetoServidor: Povoamento.MaxNoServidor);
				Checa("com o teto de ZONA em 1 (e as zonas cheias), a manutencao nao enfileira ninguem",
					  _filaDoPovoamento.Count == antes, $"{_filaDoPovoamento.Count - antes} pedidos");

				// E O CONTROLE: com o teto de verdade ela tambem nao enfileira, porque a populacao ja
				// esta completa -- entao a linha de cima podia estar passando por outro motivo. O que
				// separa os dois casos e uma zona VAZIA: la o teto apertado recusa e o teto normal
				// aceita, com a MESMA populacao e a MESMA funcao.
				Checa("...e `CabeMaisUmCorpo` responde SIM na zona vazia com o teto normal e NAO com o "
					+ "teto apertado -- entao a recusa e o teto, e nao a populacao ja completa",
					  CabeMaisUmCorpo(zonaVazia, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out _)
					  && !CabeMaisUmCorpo(zonaVazia, 1, Povoamento.MaxNoServidor, out string p2),
					  "");

				Checa("...e o teto do SERVIDOR tambem dispara (a conta inclui a fila)",
					  !CabeMaisUmCorpo(zonaVazia, Povoamento.MaxPorZona, tetoServidor: 1, out _));

				// O PLANO DE PRODUCAO CABE. Se um dia ele passar do teto, esta linha reprova antes de
				// alguem descobrir em jogo que um planeta para de encher pela metade.
				Checa($"o plano de producao ({_moldes.Plano.Sum(l => l.Quantos)}) cabe nos tetos "
					+ $"({Povoamento.MaxPorZona}/zona, {Povoamento.MaxNoServidor}/servidor)",
					  _moldes.Plano.All(l => l.Quantos <= Povoamento.MaxPorZona)
					  && _moldes.Plano.Sum(l => l.Quantos) <= Povoamento.MaxNoServidor);
			}

			// =====================================================================
			// 8. MORRER TIRA DO MUNDO -- e nao ressuscita na Terra
			// =====================================================================
			{
				ServerPlayer? vitima = _players.Values.FirstOrDefault(
					p => p.Papel?.Tipo == TipoDeNpc.Cidadao
					  && !string.Equals(p.Berco.Natal, SpawnZone.Name, StringComparison.OrdinalIgnoreCase));

				if (vitima == null) Checa("ha um habitante fora da Terra pra matar", false);
				else
				{
					int id = vitima.Id;
					string natal = vitima.Berco.Natal;
					int antes = ContarHabitantes(natal, vitima.Papel!.Molde.Id);

					vitima.Ficha.dead = true;
					vitima.RelogioDaMorte = 0;              // a carencia ja venceu
					TickCombate(Protocol.TickSeconds);

					Checa($"o habitante morto de '{natal}' SAIU do mundo", !_players.ContainsKey(id));
					Checa("...e nao apareceu na Terra (era o `Renascer` mandando corpo sem berco pra `SpawnZone`)",
						  !_players.Values.Any(p => p.Id == id));
					Checa("...e a conta do planeta caiu (a manutencao vai repor OUTRO, em 5 min)",
						  ContarHabitantes(natal, vitima.Papel.Molde.Id) == antes - 1);
					forjados.Remove(vitima);
				}
			}

			// =====================================================================
			// 9. DETERMINISMO -- a ficha continua sendo funcao pura de (universo, molde, lugar)
			// =====================================================================
			{
				MoldeDeNpc cid = _moldes.Get("cidadao")!;
				ulong sem = SorteioDeNpc.SementeDe(SeedDoUniverso, cid.Id, 4242);
				string Assinar(FichaSorteada f) => $"{f.Raca}/{f.Classe}/{f.Genero}/{f.Nome}/{f.Ficha.BP:0.000}";
				Checa("o mesmo lugar da o mesmo habitante",
					  Assinar(SorteioDeNpc.Sortear(cid, sem, _racas, _skills, "Namek", 0))
					  == Assinar(SorteioDeNpc.Sortear(cid, sem, _racas, _skills, "Namek", 0)));
				Checa("...e o mesmo lugar em OUTRO planeta da outro habitante (o planeta entra na raca)",
					  Assinar(SorteioDeNpc.Sortear(cid, sem, _racas, _skills, "Namek", 0))
					  != Assinar(SorteioDeNpc.Sortear(cid, sem, _racas, _skills, "Earth", 0)));
			}

			// =====================================================================
			// 10. O CUSTO, E O MOB-ZUMBI -- 5400 tiques com a populacao de producao
			// =====================================================================
			MedirCusto(Checa);

			Checa("a bancada chegou ao fim (ver o `catch`: sem esta linha, abortar no meio "
				+ "reportava '0 falhas')", true, "");
		}
		// ============================ ABORTAR NO MEIO NAO PODE PARECER SUCESSO ============================
		// Sem este `catch`, uma excecao no meio subia pro tratador de pacotes (que a engole com um
		// PushWarning), o `finally` imprimia "FIM: 43 ok, 0 falha(s)" e a bancada parecia ter passado.
		// Aconteceu de verdade na primeira rodada -- o palco sorteado foi um mapa sem raca nenhuma com
		// berco nele --, e o unico sinal era a contagem de checagens ser menor que a de sempre.
		//
		// Uma bancada que fica verde ao ser interrompida e pior que bancada nenhuma, porque ninguem
		// mais olha o numero. Agora a excecao E uma falha, com a pilha junto.
		// ============================================================================================
		catch (Exception ex)
		{
			Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString());
		}
		finally
		{
			foreach (ServerPlayer f in forjados) if (_players.ContainsKey(f.Id)) RemoverNpc(f);
			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada de povoamento: {falhou} falha(s)");
			Avisar(pl, $"bancada de povoamento: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	/// <summary>
	/// O CIDADAO NAO FECHA A DISTANCIA; O CHEFE FECHA. **Medido em pixels, ao longo de 600 tiques.**
	///
	/// ============================ POR QUE DISTANCIA, E NAO "ELE ATACOU?" ============================
	/// Contar socos mediria o alcance do braco: um corpo hostil que nunca chegasse perto tambem daria
	/// zero socos, e a checagem passaria pelo motivo errado. A distancia mede a INTENCAO -- um corpo
	/// que decidiu cacar anda na direcao do alvo desde o primeiro tique, muito antes de encostar.
	///
	/// Os dois comecam a 12 tiles do alvo (fora de qualquer alcance de soco) e ninguem apanha: o que
	/// se ve e so a decisao. E o alvo tem `Peer` de verdade -- e ele que faz a zona contar como
	/// habitada, senao o cidadao congelaria e a checagem passaria por ANTI-LAG e nao por pacifismo.
	/// ==========================================================================================
	/// </summary>
	private void MedirPacifismo(Action<string, bool, string> Checa, List<ServerPlayer> forjados)
	{
		ServerPlayer? alvo = _players.Values.FirstOrDefault(p => p.Peer != null);
		if (alvo == null) { Checa("ha um jogador de verdade pra servir de alvo", false, ""); return; }

		// ============================ A MEDIDA PRECISA DE UM PALCO VAZIO ============================
		// A primeira versao mediu na Terra, com os 40 habitantes ja nascidos -- e o CONTROLE reprovou:
		// o chefe nao foi atras do jogador porque `PresaDaFera` devolve **o corpo em pe mais proximo,
		// seja quem for**, e o mais proximo era um cidadao. Ele cacou, so que outra pessoa.
		//
		// Isso e o comportamento certo do hostil e a medida errada: com quarenta corpos no palco, a
		// distancia ate o jogador nao diz nada sobre a decisao de ninguem. Entao o palco e um mapa
		// pre-feito VAZIO, e o jogador vai pra la -- pelo `MoveToZone`, que e o caminho de producao.
		// ======================================================================================
		// ============================ O PALCO PRECISA DE GENTE QUE POSSA NASCER NELE ============================
		// Vazio nao basta: o molde `cidadao` nao declara racas, entao ele so nasce onde ALGUMA raca
		// tem berco. A primeira versao pegava o primeiro pre-feito vazio e caiu em "Arconia" -- um
		// mapa sem berco nenhum --, e o `Sortear` fez o que foi mandado fazer: gritou em vez de
		// inventar um humano. A bancada e que estava pedindo o impossivel.
		// ==================================================================================================
		// E OS CANDIDATOS SAO OS BERCOS, e nao os corpos celestes. `Espaco.PreFeitos()` deixa o Paraiso
		// e o Inferno DE FORA de proposito (no DM eles sao `area/afterlifeareas`, nao planetas -- ver
		// o cabecalho da `PlanetaNatal`), e eram justamente os dois mapas que sobravam vazios. Sem
		// eles a busca caia de volta na Terra, com os 40 habitantes dentro, e o controle reprovava
		// pelo motivo certo: o chefe cacou -- so que um cidadao, que estava mais perto.
		string[] racasConhecidas = [.. _racas!.Protos.Keys];
		ZoneKey palco = ZoneKey.Premade(
			racasConhecidas.Select(Bercos.PlanetaNatal).Distinct(StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault(n => ZoneList(ZoneKey.Premade(n).Hash).Count == 0
								  && _catalogo?.Get(ZoneKey.Premade(n)) != null)
			?? alvo.Zone.Name);

		ZoneKey voltaZona = alvo.Zone;
		Vec2 voltaPos = alvo.Pos;
		Vec2 centro = MapaDaZonaOuCatalogo(palco)?.PontoLivrePerto(SpawnPos) ?? SpawnPos;
		MoveToZone(alvo.Id, palco, centro);

		const float doze = 12 * ZoneCollision.TileSize;
		ZoneKey zona = palco;

		ServerPlayer? Por(string molde, float dx, ulong lugar)
		{
			Vec2 onde = MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(alvo.Pos + new Vec2(dx, 0))
					 ?? alvo.Pos + new Vec2(dx, 0);
			ServerPlayer? n = NascerNpc(molde, zona, onde, lugar);
			if (n == null) return null;
			n.Combate.Letal = false;    // a bancada nao mata ninguem
			forjados.Add(n);
			return n;
		}

		ServerPlayer? cidadao = Por("cidadao", doze, 950_001);
		ServerPlayer? chefe = Por("guardiao_saiyajin", -doze, 950_002);
		if (cidadao == null || chefe == null)
		{
			Checa("os dois corpos da medida nasceram", false, "");
			MoveToZone(alvo.Id, voltaZona, voltaPos);
			return;
		}

		GD.Print($"  ---- palco: '{palco.Name}', so o jogador e os dois corpos ----");
		float distCidadao0 = (cidadao.Pos - alvo.Pos).Length;
		float distChefe0 = (chefe.Pos - alvo.Pos).Length;

		for (int t = 0; t < 600; t++) TickDosCorposSemDono(Protocol.TickSeconds);

		float distCidadao1 = (cidadao.Pos - alvo.Pos).Length;
		float distChefe1 = (chefe.Pos - alvo.Pos).Length;

		GD.Print($"  ---- 600 tiques (20 s): cidadao {distCidadao0:0} -> {distCidadao1:0} px, "
			   + $"chefe {distChefe0:0} -> {distChefe1:0} px ----");

		// ============================ A COMPARACAO E COM A PROPRIA PARTIDA, NAO ENTRE OS DOIS ============================
		// A primeira versao perguntava "o cidadao esta mais longe que o chefe?" e reprovou com o
		// chefe a 417 px e o cidadao a 384 -- o chefe tinha FECHADO 36 px e ainda assim estava mais
		// longe, porque os dois nasceram em pontos livres diferentes. Comparar dois corpos mede o
		// terreno; comparar cada um com onde ELE comecou mede a decisao.
		// =========================================================================================================
		Checa("o CHEFE fecha a distancia sozinho (o controle: a IA de caca funciona)",
			  distChefe1 < distChefe0 - 32, $"{distChefe0:0} -> {distChefe1:0}");
		Checa("o CIDADAO nao se aproxima de ninguem em 20 s (o `AIAlwaysActive = 0` do mob/npc/Citizen)",
			  distCidadao1 > distCidadao0 - 32, $"{distCidadao0:0} -> {distCidadao1:0}");
		Checa("...e ele nao tem alvo nenhum enquanto ninguem encosta nele",
			  PresaDoNpc(cidadao) == null, "");
		Checa("...e o jogador nao levou um arranhao dele", alvo.Ficha.HP > 99, $"HP {alvo.Ficha.HP:0.0}");

		// ---- E AGORA ALGUEM BATE NELE ------------------------------------
		// Pelo FUNIL de producao (`MarcarAgressao`), que e o que o soco, a rajada e o Zanzo Clash
		// chamam. Escrever `UltimoAgressor`/`RancorAte` na mao aqui testaria os campos, e nao a regra.
		MarcarAgressao(cidadao, alvo);
		Checa("apanhou: agora ele TEM alvo (o `refresh_combat_tag` -> `provoke` do original)",
			  PresaDoNpc(cidadao) == alvo, "");

		float antes = (cidadao.Pos - alvo.Pos).Length;
		for (int t = 0; t < 600; t++) TickDosCorposSemDono(Protocol.TickSeconds);
		float depois = (cidadao.Pos - alvo.Pos).Length;

		GD.Print($"  ---- provocado: {antes:0} -> {depois:0} px ----");
		Checa("...e ele vem pra cima (pacifico ate apanhar, e nao pacifista)",
			  depois < antes - 32, $"{antes:0} -> {depois:0}");

		// ---- E O RANCOR VENCE ---------------------------------------------
		// O prazo E o estado: nao ha campo de alvo pra limpar. `combat_tag_duration` = 90 s.
		cidadao.RancorAte = NowMs() - 1;
		Checa($"e o rancor vence sozinho depois de {Povoamento.SegundosDeRancor:0} s -- ele volta a "
			+ "ser habitante", PresaDoNpc(cidadao) == null, "");

		// ============================ O PASSEIO TEM DOIS JEITOS DE ESTAR ERRADO ============================
		// A primeira versao desta camada mandava o `RumoDaFera` pro habitante ocioso e a bancada mediu
		// **2400 px em 20 s** -- 75 tiles: em dez minutos o vilarejo teria se espalhado pelo mapa.
		//
		// O conserto tem um modo de falha proprio e do lado oposto: se a janela de passo for curta
		// demais pra a velocidade de um habitante, ele nunca sai do lugar -- e "cidade de estatuas"
		// tambem passaria num teto que so olha pra cima. Entao a checagem tem as DUAS bordas, e a
		// janela e de 2 minutos porque a cadencia do original e de um passo a cada ~20 s: medir 20 s
		// daria zero na maioria das vezes so pelo sorteio.
		// =============================================================================================
		Vec2 antesDoPasseio = cidadao.Pos;
		cidadao.UltimoAgressor = 0;
		cidadao.RancorAte = 0;
		double andado = 0;
		for (int t = 0; t < 3600; t++)
		{
			Vec2 antesDoTique = cidadao.Pos;
			TickDosCorposSemDono(Protocol.TickSeconds);
			andado += (cidadao.Pos - antesDoTique).Length;   // o CAMINHO, e nao o deslocamento liquido
		}
		float deslocou = (cidadao.Pos - antesDoPasseio).Length;
		GD.Print($"  ---- passeio ocioso em 2 min: andou {andado:0} px "
			   + $"({andado / ZoneCollision.TileSize:0.0} tiles), saiu {deslocou:0} px do lugar ----");

		Checa("o habitante ocioso SE MEXE (a cidade nao e um cenario de estatuas)",
			  andado > 0, $"{andado:0} px em 2 min");
		Checa("...e nao vagueia: menos de 20 tiles ANDADOS em 2 min (o `prob(35)` + `step_rand` do "
			+ "idle_wander_loop da ~1 tile a cada 20 s; o rumo da fera daria 450)",
			  andado < 20 * ZoneCollision.TileSize, $"{andado / ZoneCollision.TileSize:0.0} tiles");

		MoveToZone(alvo.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// O CUSTO COM O NUMERO DE PRODUCAO, e o mob-zumbi.
	///
	/// ============================ UM PONTO NAO MEDE VAZAMENTO ============================
	/// Mesmo desenho da secao 17 da bancada da IA, e pelo mesmo motivo escrito la: o mob-zumbi nao e
	/// um tique caro, e um tique que FICA caro. O eixo e o TIQUE (o estado que vaza vaza por tique),
	/// sao 5400 deles -- tres minutos de jogo a 30 Hz -- em seis janelas, e o criterio compara o
	/// terco final com o terco inicial.
	///
	/// A diferenca pra aquela secao e o QUE esta vivo: la sao 20 corpos forjados brigando; aqui e a
	/// **populacao de producao inteira**, nascida pelo caminho de producao, espalhada pelos planetas
	/// do plano -- que e a unica configuracao que responde "quanto custa o povoamento".
	/// ================================================================================
	/// </summary>
	private void MedirCusto(Action<string, bool, string> Checa)
	{
		int corpos = CorposSemDonoNoServidor();
		const int janelas = 6, tiquesPorJanela = 900;
		var us = new double[janelas];
		var lixo = new double[janelas];

		for (int j = 0; j < janelas; j++)
		{
			double soma = 0; long bytes = 0;
			for (int t = 0; t < tiquesPorJanela; t++)
			{
				long b0 = GC.GetAllocatedBytesForCurrentThread();
				ulong t0 = Time.GetTicksUsec();
				TickDoPovoamento(Protocol.TickSeconds);
				TickDosCorposSemDono(Protocol.TickSeconds);
				TickCombate(Protocol.TickSeconds);
				soma += Time.GetTicksUsec() - t0;
				bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
			}
			us[j] = soma / tiquesPorJanela;
			lixo[j] = bytes / (double)tiquesPorJanela;
		}

		GD.Print($"  ---- {corpos} corpos sem dono, 5400 tiques (3 min de jogo), janelas de 900 ----");
		for (int j = 0; j < janelas; j++)
			GD.Print($"       janela {j + 1}: {us[j]:0.0} us/tique ({us[j] / 333.0:0.00}% do orcamento), {lixo[j]:0} B/tique");

		// O MESMO CRITERIO DA BANCADA DA IA: terco final contra terco inicial, folga de 1,5x e piso
		// absoluto. Vazamento de verdade nao cresce 50% -- ele cresce ordens de grandeza.
		static bool Progrediu(double[] j)
		{
			int terco = Math.Max(1, j.Length / 3);
			return j.Skip(j.Length - terco).Average() > j.Take(terco).Average() * 1.5 + 10;
		}

		Checa($"o tique NAO fica mais caro com o tempo (janela 1 {us[0]:0.0} us -> janela {janelas} "
			+ $"{us[^1]:0.0} us, com {corpos} corpos vivos)",
			  !Progrediu(us), string.Join(" -> ", us.Select(v => $"{v:0.0}")));
		Checa("...e o LIXO por tique tambem nao",
			  !Progrediu(lixo), string.Join(" -> ", lixo.Select(v => $"{v:0}")));

		// ============================ "TANTOS QUANTO ANTES" SERIA A PERGUNTA ERRADA ============================
		// A primeira versao comparava a contagem do fim com a do comeco e reprovou com "152 de 151" --
		// e o 152 estava CERTO: a secao 8 matou um habitante e a manutencao repos outro no meio dos
		// 5400 tiques, que e exatamente o que ela existe pra fazer. Um numero que sobe nao e um mob-zumbi.
		//
		// O que o mob-zumbi produz e um corpo que PARA de ser dirigido sem nada dizendo -- entao a
		// pergunta certa e essa, e ela nao depende de contagem nenhuma.
		// ================================================================================================
		int estatuas = _players.Values.Count(p => p.Papel != null && p.Cerebro == null && !p.Ficha.dead);
		Checa("nenhum corpo virou estatua em 3 min (todo NPC vivo continua sendo dirigido)",
			  estatuas == 0, $"{estatuas} sem cerebro");
		Checa("...e a manutencao repos o habitante que a secao 8 matou (ela roda de verdade, no tique)",
			  CorposSemDonoNoServidor() >= corpos, $"{CorposSemDonoNoServidor()} contra {corpos}");
		Checa("...e a fila do povoamento nao inchou (a manutencao completa, ela nao acumula)",
			  _filaDoPovoamento.Count == 0, $"{_filaDoPovoamento.Count} pedidos pendurados");
		Checa("...e a lista de dirigidos e reusada, nao acumulada",
			  _dirigidos.Count == _players.Values.Count(p => p.Cerebro != null),
			  $"{_dirigidos.Count}");

		// ============================ E O CRITERIO TEM DENTES? ============================
		// Mesmo defeito injetado da bancada da IA: uma lista que so cresce e e varrida todo tique --
		// a forma literal do mob-zumbi que o DM pagou (`NPCAI.dm:751`). Se o `Progrediu` nao acusar
		// isto, ele nao acusaria nada, e o verde de cima seria decoracao.
		// ==============================================================
		{
			var zumbis = new List<ServerPlayer>();
			var comVazamento = new double[janelas];
			long lixeira = 0;
			List<ServerPlayer> vivos = [.. _players.Values.Where(p => p.Papel != null)];
			if (vivos.Count > 0)
			{
				for (int j = 0; j < janelas; j++)
				{
					double soma = 0;
					for (int t = 0; t < 150; t++)
					{
						ulong t0 = Time.GetTicksUsec();
						TickDosCorposSemDono(Protocol.TickSeconds);
						for (int k = 0; k < 60; k++) zumbis.Add(vivos[k % vivos.Count]);
						foreach (ServerPlayer z in zumbis) lixeira += z.Id;
						soma += Time.GetTicksUsec() - t0;
					}
					comVazamento[j] = soma / 150.0;
				}
				_ = lixeira;

				GD.Print($"       (injetado) com mob-zumbi: {string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}"))} us");
				Checa("DEFEITO INJETADO: com uma lista que so cresce e e varrida todo tique, o MESMO "
					+ "criterio REPROVA -- entao o verde de cima e medida, e nao teto largo",
					  Progrediu(comVazamento), string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}")));
			}
		}
	}
}
