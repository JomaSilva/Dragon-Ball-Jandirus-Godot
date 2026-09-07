using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O POVOAMENTO EM JOGO: os planetas ganham habitantes, e os repoem sozinhos.
///
/// ============================ O QUE FALTAVA NAO ERA A FABRICA -- ERA O PEDIDO ============================
/// `NascerNpc` existia, compilava e era exercitada por duas bancadas, e mesmo assim **nao havia um
/// unico NPC em jogo**: os dois unicos chamadores dela eram `--npcteste` e `--iateste`. Uma fabrica
/// sem pedido. Este arquivo e o pedido.
/// ====================================================================================================
///
/// ============================ A RACA VEM DO BERCO, E NAO DE UMA TABELA NOVA ============================
/// O plano do `npcs.json` diz **quantos** e **onde**; quem diz **quem** e a
/// <see cref="Jandirus.Core.Races.Bercos.RacasNascidasEm"/>, que e a `PlanetaNatal` -- a mesma regra
/// que decide onde o jogador nasce -- lida ao contrario. Nenhum arquivo deste sistema contem a
/// palavra "Human" ao lado da palavra "Earth".
///
/// A consequencia interessante e que o povoamento fica CERTO em planetas que o DM nunca povoou: um
/// cidadao de Icer e um Frost Demon porque o berco do Frost Demon e Icer, e ninguem escreveu isso.
/// ==================================================================================================
///
/// ============================ E TRES REGRAS DE CUSTO, TODAS DO ORIGINAL ============================
///   1. NASCE ESPALHADO       -- `sleep(1)` entre cada NPC (PlanetPopulation.dm:431). Sortear uma
///                               ficha custa 0,2-0,3 ms; 148 de uma vez seriam 30 ms num tique so.
///   2. REPOE DE 5 EM 5 MIN   -- `sleep(3000)` do `Population_Maintenance_Loop` (:467). E COMPLETA
///                               ate o alvo, nunca mata pra chegar nele.
///   3. CONGELA SEM PLATEIA   -- o `planet_has_players` (:129), que mora no `MenteDormindo`
///                               (passo 0 do `TicarUmCorpo`), com o embaralho de posicoes como
///                               contrapartida: ver `GameServer.Embaralho.cs`.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A FILA DE NASCIMENTO. Cada item e um pedido -- molde, zona, e o LUGAR que vira semente.
	///
	/// A fila existe pela regra 0.4: a manutencao decide de uma vez (uma volta por linha do plano,
	/// barata) e o nascimento -- que e o caro -- e drenado a
	/// <see cref="Povoamento.NascimentosPorTique"/> por tique.
	/// </summary>
	private readonly Queue<(string Molde, ZoneKey Zona, ulong Lugar)> _filaDoPovoamento = new();

	/// <summary>Quando roda a proxima manutencao (segundos de servidor). Zero = na primeira volta.</summary>
	private double _proximaManutencao;

	/// <summary>Relogio proprio do povoamento, em segundos. Mesma disciplina do `_relogioDoEstomago`.</summary>
	private double _relogioDoPovoamento;

	/// <summary>
	/// O CONTADOR DE LUGARES, por (planeta, molde). E o terceiro argumento da
	/// <see cref="SorteioDeNpc.SementeDe"/>, e ele **nunca volta atras**.
	///
	/// ============================ POR QUE ELE NAO E O INDICE DA VAGA ============================
	/// A tentacao e usar "a vaga 7 de Vegeta", que daria determinismo perfeito: o mesmo servidor com
	/// a mesma semente teria sempre os mesmos 40 habitantes, inclusive depois de um reinicio.
	///
	/// E daria tambem o oposto do que a morte significa: matar o cidadao da vaga 7 faria a manutencao
	/// repor **exatamente o mesmo cidadao**, mesmo nome, mesma cara, mesmo BP, cinco minutos depois.
	/// O DM nao faz isso -- la cada `make_saiyan_commoner` sorteia do zero -- e a razao aparece no
	/// jogo: uma populacao que se repoe identica nao e uma populacao, e um cenario.
	///
	/// Entao a semente e monotonica. O determinismo que se perde e o de "o habitante numero 7 da
	/// Terra"; o que se mantem e o que a regra 0.2 pede de verdade -- a ficha continua sendo funcao
	/// pura de (seed do universo, molde, lugar), e a bancada afirma isso pedindo o mesmo lugar duas
	/// vezes.
	/// ======================================================================================
	/// </summary>
	private readonly Dictionary<string, ulong> _lugaresDoPovoamento = new(StringComparer.Ordinal);

	/// <summary>
	/// QUANTOS HABITANTES VIVOS este planeta tem, deste molde. E o `count_citizens`
	/// (PlanetPopulation.dm:420), inclusive na parte que importa: **morto nao conta**.
	///
	/// Varre `_players` e nao a lista da zona porque o corpo pode ter sido levado pra outra zona
	/// (arremesso pro espaco, admin) -- ele continua sendo um habitante DAQUELE planeta, e o
	/// `Berco.Natal` e quem diz isso. Contar pela zona faria o planeta repor um cidadao toda vez que
	/// alguem tirasse um habitante de la, e a populacao cresceria sem teto.
	/// </summary>
	private int ContarHabitantes(string planeta, string moldeId)
	{
		int n = 0;
		foreach (ServerPlayer p in _players.Values)
			if (EhNpcDoMundo(p) && !p.Ficha.dead
				&& string.Equals(p.Papel!.Molde.Id, moldeId, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(p.Berco.Natal, planeta, StringComparison.OrdinalIgnoreCase))
				n++;
		return n;
	}

	/// <summary>
	/// Quantos corpos sem dono ha numa zona -- o teto de lotacao le isto.
	///
	/// A conta e `EhNpcDoMundo` e nao "sem `Peer`", e a diferenca importa aqui mais que em qualquer
	/// outro lugar: o clone da mente e o boneco do corpo largado tambem nao tem dono na tela, e contar
	/// os dois faria o teto de lotacao recusar habitante por causa de quem esta MEDITANDO.
	/// </summary>
	private int CorposSemDonoNaZona(ZoneKey zona)
	{
		int n = 0;
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (EhNpcDoMundo(p) && !p.Ficha.dead) n++;
		return n;
	}

	/// <summary>Quantos corpos sem dono ha no servidor inteiro. Ver <see cref="CorposSemDonoNaZona"/>.</summary>
	private int CorposSemDonoNoServidor()
	{
		int n = 0;
		foreach (ServerPlayer p in _players.Values)
			if (EhNpcDoMundo(p) && !p.Ficha.dead) n++;
		return n;
	}

	/// <summary>
	/// **CABE MAIS UM CORPO?** O teto, numa funcao so.
	///
	/// ============================ ELE E PERGUNTADO DUAS VEZES, E TEM QUE SER O MESMO ============================
	/// A manutencao pergunta pra decidir se ENFILEIRA; o dreno pergunta de novo pra decidir se
	/// NASCE. Duas perguntas sao necessarias -- entre uma e outra passam ate 5 s, e nesse meio-tempo
	/// um admin pode ter enchido a zona ou uma saga pode ter posto um chefe la --, mas as duas tem
	/// que ser a MESMA conta. Escritas separadas, elas divergiriam no dia em que alguem mudasse uma,
	/// e o sintoma seria uma fila que enche e um dreno que recusa pra sempre, gritando por tique.
	///
	/// A FILA CONTA como corpo: 148 pedidos enfileirados sao 148 corpos que vao existir. Sem isso a
	/// manutencao encheria a fila inteira antes do primeiro nascimento e o teto so seria descoberto
	/// no fim -- que e o teto disparando tarde demais pra evitar o custo.
	/// ======================================================================================================
	/// </summary>
	/// <param name="tetoZona">Ver <see cref="Manutencao"/> -- parametro pra a bancada poder aperta-lo.</param>
	private bool CabeMaisUmCorpo(ZoneKey zona, int tetoZona, int tetoServidor, out string porque)
	{
		int naZona = CorposSemDonoNaZona(zona);
		int naFilaDaZona = 0, naFila = _filaDoPovoamento.Count;
		foreach ((_, ZoneKey z, _) in _filaDoPovoamento) if (z.Hash == zona.Hash) naFilaDaZona++;

		if (naZona + naFilaDaZona >= tetoZona)
		{
			porque = $"'{zona.Name}' esta no teto de lotacao ({tetoZona} corpos sem dono; "
				   + $"{naZona} no mundo + {naFilaDaZona} na fila)";
			return false;
		}

		int noServidor = CorposSemDonoNoServidor() + naFila;
		if (noServidor >= tetoServidor)
		{
			porque = $"teto do servidor atingido ({tetoServidor} corpos sem dono; {noServidor} contados)";
			return false;
		}

		porque = "";
		return true;
	}

	/// <summary>
	/// A MANUTENCAO. Roda a cada <see cref="Povoamento.SegundosEntreManutencoes"/> e na primeira
	/// volta -- que e o povoamento inicial, e nao um caminho a parte
	/// (`Build_Planet_Population` do DM tambem chama o mesmo `Populate_All_Planets`).
	///
	/// Ela nao NASCE ninguem: ela ENFILEIRA. Ver <see cref="_filaDoPovoamento"/>.
	/// </summary>
	/// <param name="tetoZona">
	/// O teto de lotacao. Parametro com padrao pelo mesmo motivo do `teto` do
	/// <see cref="Jandirus.Core.Races.Bercos.ServeDeBerco"/>: a regra 0.7 diz que um teto que nunca
	/// dispara e indistinguivel de teto nenhum, e a unica forma honesta de a bancada ver o caminho de
	/// recusa e apertar o numero contra o codigo de PRODUCAO. Em jogo ninguem passa este argumento.
	/// </param>
	private void Manutencao(int tetoZona = Povoamento.MaxPorZona, int tetoServidor = Povoamento.MaxNoServidor)
	{
		if (_moldes == null) return;

		foreach (LinhaDePovoamento linha in _moldes.Plano)
		{
			MoldeDeNpc? molde = _moldes.Get(linha.Molde);
			if (molde == null) continue;

			// O RECORTE, DE NOVO -- e ele nao e redundante com o do `NascerNpc`. La ele impede o
			// corpo de existir; aqui ele impede a FILA de encher com pedidos que serao recusados um a
			// um pra sempre, um por tique, gritando no console. O plano de producao so tem cidadao,
			// entao esta linha nunca dispara hoje: ela existe pro dia em que alguem puser um molde de
			// inimigo no plano antes de o dono religar o interruptor.
			if (!Povoamento.PodeNascer(molde.Tipo)) continue;

			var zona = ZoneKey.Premade(linha.Planeta);

			// ============================ A ZONA TEM QUE EXISTIR ============================
			// Sem mapa nao ha colisao, e sem colisao o `PontoDeNascimento` devolveria o (249,250) cru
			// -- que em Icer e PAREDE (medido no `.col`, ver `GameServer.Berco.cs`). Um planeta que
			// nao esta no manifesto e erro de dado, e erro de dado grita.
			if (_catalogo?.Get(zona) == null)
			{
				GD.PushError($"[server] povoamento: a zona '{linha.Planeta}' nao esta no manifesto de mapas");
				continue;
			}

			// ============================ PLANETA MORTO NAO SE REPOVOA ============================
			// Sem esta linha o sistema inteiro de destruicao se desfazia sozinho: o `limit_life()`
			// mata os 40 cidadaos de Vegeta ao longo dos vinte minutos do pavio, e a manutencao
			// seguinte -- cinco minutos depois -- os enfileira de volta, um por um, porque ela so
			// pergunta "quantos faltam pro alvo?".
			//
			// O corte e no CONDENADO e nao no DESTRUIDO: repovoar um planeta que esta agonizando
			// seria igualmente absurdo, e e a fase em que o defeito apareceria primeiro. Ver
			// `GameServer.Destruicao.cs`.
			if (ZonaCondenada(zona))
			{
				GD.Print($"[server] povoamento: '{linha.Planeta}' esta condenado -- ninguem nasce la");
				continue;
			}

			int vivos = ContarHabitantes(linha.Planeta, molde.Id);

			// ============================ QUEM JA ESTA NA FILA JA E DESTE PLANETA ============================
			// A conta era so a dos corpos VIVOS, e por isso ela dependia de a fila estar vazia quando a
			// manutencao roda -- o que e verdade em jogo (5 min entre manutencoes contra 5 s de dreno) e
			// deixou de ser assim que a bancada da prova adiantou o relogio da manutencao duas vezes
			// seguidas: a segunda volta viu os corpos que ainda nao tinham nascido como corpos que
			// FALTAVAM, e enfileirou 148 pedidos de novo. O mundo nasceu com 287 habitantes num plano de
			// 148 -- a Terra com 71 de 40, Vegeta parada no teto de zona.
			//
			// O `CabeMaisUmCorpo` ja contava a fila (pro TETO) e esta linha nao contava (pro ALVO), e as
			// duas respondem a mesma pergunta: *"quantos corpos vao existir aqui?"*. Duas contas
			// diferentes sobre a mesma coisa e o defeito que este projeto ja nomeou -- e a bancada nao
			// inventou o caso, ela so ADIANTOU o relogio o bastante pra ele acontecer.
			// ============================================================================================
			int naFila = 0;
			foreach ((string m, ZoneKey z, _) in _filaDoPovoamento)
				if (z.Hash == zona.Hash && string.Equals(m, molde.Id, StringComparison.OrdinalIgnoreCase))
					naFila++;

			// COMPLETA ATE O ALVO, e nunca mata pra chegar nele: o `for(var/i = have + 1 to POP_*)`
			// do original nao tem ramo de "sobrou gente".
			for (int i = vivos + naFila; i < linha.Quantos; i++)
			{
				if (!CabeMaisUmCorpo(zona, tetoZona, tetoServidor, out string porque))
				{
					GD.Print($"[server] povoamento: {porque} -- {linha.Quantos - i} cidadao(s) de "
						   + $"'{linha.Planeta}' nao nascem");
					break;
				}

				string chave = linha.Planeta + " / " + molde.Id;
				ulong lugar = _lugaresDoPovoamento.GetValueOrDefault(chave) + 1;
				_lugaresDoPovoamento[chave] = lugar;

				_filaDoPovoamento.Enqueue((molde.Id, zona, lugar));
			}
		}
	}

	/// <summary>
	/// O TIQUE DO POVOAMENTO. Duas coisas, e as duas baratas:
	///   1. o relogio da manutencao (uma comparacao);
	///   2. drenar <see cref="Povoamento.NascimentosPorTique"/> da fila.
	///
	/// Roda no tique CHEIO e nao no bloco de 1 Hz. A manutencao em si e de 5 em 5 minutos, mas o
	/// DRENO precisa da cadencia alta: e ele que espalha o custo. A 1 Hz, povoar 148 corpos levaria
	/// dois minutos e meio; a 30 Hz leva 5 s, com o mesmo custo por tique.
	/// </summary>
	private void TickDoPovoamento(double dt, int tetoZona = Povoamento.MaxPorZona,
								  int tetoServidor = Povoamento.MaxNoServidor)
	{
		if (_moldes == null) return;

		_relogioDoPovoamento += dt;
		if (_relogioDoPovoamento >= _proximaManutencao)
		{
			_proximaManutencao = _relogioDoPovoamento + Povoamento.SegundosEntreManutencoes;
			Manutencao(tetoZona, tetoServidor);
		}

		for (int i = 0; i < Povoamento.NascimentosPorTique && _filaDoPovoamento.Count > 0; i++)
		{
			(string moldeId, ZoneKey zona, ulong lugar) = _filaDoPovoamento.Dequeue();

			// ============================ A FILA ENVELHECE, E O MUNDO ANDA ============================
			// Entre enfileirar e nascer passam ate 5 s. Nesse meio-tempo alguem pode ter spawnado 60
			// corpos pelo admin, ou uma saga pode ter posto um chefe na mesma zona. O teto e conferido
			// **na hora de nascer** e nao so na hora de enfileirar -- do outro jeito ele seria uma
			// intencao registrada no passado, e a lotacao real passaria dele sem nada acusando.
			//
			// A conta e a MESMA das duas vezes (`CabeMaisUmCorpo`), e por isso o pedido SAI DA FILA
			// antes de ser conferido: aquela funcao conta a fila como corpos que vao existir, entao um
			// pedido que ainda estivesse la contaria a si mesmo e o teto dispararia um corpo cedo --
			// um planeta parando sempre um habitante antes do numero pedido, calado.
			// =====================================================================================
			if (!CabeMaisUmCorpo(zona, tetoZona, tetoServidor, out string porque))
			{
				// O RESTO DA FILA E DO MESMO PLANO. Tentar um a um so produziria a mesma recusa por
				// tique, pra sempre, gritando no console -- e a proxima manutencao (5 min) reenfileira
				// o que couber, que e o caminho certo pra reavaliar isto.
				GD.Print($"[server] povoamento: {porque} -- {_filaDoPovoamento.Count} pedido(s) descartados");
				_filaDoPovoamento.Clear();
				break;
			}

			NascerNpc(moldeId, zona, PontoDeHabitante(zona, lugar), lugar);
		}
	}

	/// <summary>
	/// ONDE UM HABITANTE APARECE -- a vaga do pedido `lugar` no <see cref="Habitat"/> da zona.
	///
	/// ============================ O QUE MUDOU, E O QUE NAO MUDOU ============================
	/// Antes: `rand(-24, 24)` tiles em volta do (249,250), o `planet_spawn_turf` do original
	/// (PlanetPopulation.dm:283) esticado. Os quarenta habitantes da Terra nasciam num quadrado
	/// de 49x49 -- e foi o que o dono viu: *"spawn de NPCs distribuido pelo planeta inteiro (so em
	/// terra, navegavel, sem colisao)"*. Agora e o habitat: terra navegavel ligada ao berco, em
	/// regioes, com distancia minima (`Core/Npc/Habitat.cs`). DIVERGENCIA DECLARADA do original,
	/// a pedido.
	///
	/// O que NAO mudou e a promessa que o Embaralho escreveu por extenso: **funcao pura de (semente
	/// do universo, zona, lugar)**. A ordem das vagas do habitat e fixa pela semente, e `lugar` so
	/// indexa nela; nenhum corpo vivo entra na conta. O `PontoLivrePerto` do fim e o de sempre: a
	/// vaga foi levantada UMA vez, e uma obra pode ter subido nela depois.
	/// ========================================================================================
	/// </summary>
	private Vec2 PontoDeHabitante(ZoneKey zona, ulong lugar)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		Habitat? h = mapa == null ? null : HabitatDaZona(zona);
		if (mapa == null || h == null || h.Vagas == 0) return PontoDeNascimento(zona);
		(int cx, int cy) = h.Vaga(lugar);
		return mapa.PontoLivrePerto(mapa.CentroDaCelula(cx, cy));
	}
}
