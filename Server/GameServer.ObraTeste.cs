using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA CHAVE DE ZONA DAS CONSTRUCOES -- `--obrateste`.
///
/// ============================ POR QUE ELA EXISTE ============================
/// A `Obra` guardava a zona como STRING DE NOME e a carga a remontava com `ZoneKey.Premade(...)`.
/// Isso nao dava erro: **dava dado errado, calado**. Uma bancada erguida num planeta gerado voltava
/// do disco numa zona pre-feita que nao existe (sumia do mundo), e dois planetas gerados homonimos
/// -- que existem, porque o nome `{bioma}-{|Sx|%1000}{|Sy|%1000}{k}` PERDE O SINAL da celula --
/// dividiam as mesmas obras.
///
/// Defeito calado se prova com o DISCO, e nao com a memoria: o crivo em memoria concordava consigo
/// mesmo o tempo todo. Por isso esta bancada passa pelo `GravarMundoEm`/`CarregarMundoDeDisco` DE
/// PRODUCAO -- os mesmos dois metodos que o boot chama --, so que apontados pra um arquivo dela.
/// ============================================================================
///
/// ============================ AS CINCO PRIMEIRAS SAO DE CRIVO; AS DUAS ULTIMAS SAO DE MUNDO ============================
/// As secoes 1 a 5 montam `ZoneKey`s NA MAO e provam o crivo. Elas sao necessarias e nao sao
/// suficientes, e o motivo e desconfortavel: **uma chave inventada concorda com quem a inventou**.
/// `ZoneKey.Procedural("Deserto-120", 111)` prova que o filtro separa duas chaves diferentes -- nao
/// prova que o universo de producao PRODUZ duas chaves diferentes pro mesmo nome, nem que a chave
/// que o POUSO calcula amanha e a mesma que a CONSTRUCAO gravou ontem. Se o gerador e o pouso
/// discordarem em um bit, as cinco primeiras continuam verdes e a bancada some do mundo do jogador.
///
/// Por isso as secoes 6 e 7 nao inventam nada: elas varrem o universo de VERDADE
/// (<see cref="SeedDoUniverso"/>), pousam pelo funil de producao (`TickDoEspaco` ->
/// `PousarEmProcedural`), erguem pelo verbo de producao (`construir` + `posicionar`) e voltam a
/// perguntar pelo crivo de producao (`ObraNaCelula`). A bancada nao escreve uma `ZoneKey` sequer.
/// ==================================================================================================================
///
/// AS SETE SECOES:
///  1. A MIGRACAO -- o `mundo.json` de ontem (zona por nome) tem que CONVERTER, e nao sumir; e a
///     porta tem que fechar sozinha na primeira gravacao.
///  2. O PLANETA GERADO -- tipo e seed sobrevivem ao disco.
///  3. OS HOMONIMOS -- dois mundos sorteados de mesmo nome nao dividem obra, e quem responde e um
///     crivo DE PRODUCAO (`ObraNaCelula`), nao uma copia do filtro escrita aqui.
///  4. O CAMPO MORTO -- o arquivo novo nao carrega mais o campo antigo, e a `ZoneKey` montada nao
///     e gravada junto das partes.
///  5. O INTERIOR DE NAVE -- duas naves nao compartilham mais zona, que era o outro sintoma.
///  6. O UNIVERSO DE VERDADE -- pousar, erguer, REINICIAR (com o mundo descarregado) e pousar de
///     novo: a obra tem que estar la, na celula certa, num mundo que nasceu duas vezes.
///  7. OS HOMONIMOS DE VERDADE -- o par que existe no universo de producao, achado por varredura e
///     nao afirmado: duas bancadas erguidas em dois mundos de MESMO NOME nao se misturam.
///
/// ============================ COMO CADA FAMILIA REPROVA (MEDIDO, NAO SUPOSTO) ============================
/// Os tres defeitos abaixo foram INJETADOS no codigo de producao, um por vez, e o placar e o que a
/// bancada imprimiu. O placar limpo e **38 OK, 0 falhas**.
///
///  * **A CHAVE PERDE A SEED** (`Obra.PorZona` gravando `ZonaSeed = 0`) -> **28 OK, 10 falhas**. Cai
///    a secao 2 ("na MESMA zona"), as duas metades da 3, a 5 (os interiores de nave viram um so) e,
///    nas 6 e 7, a obra **some do mundo**: "a celula devolveu nada". Repare no modo de falha -- com a
///    seed zerada a obra nao se mistura com a vizinha, ela deixa de existir pra todo mundo.
///
///  * **O CRIVO VOLTA A COMPARAR POR NOME** (`ObraNaCelula` com `o.Zona.Name == zona.Name`, que e
///    literalmente o filtro de ontem) -> **35 OK, 3 falhas**, e e a OUTRA metade do estrago: agora as
///    obras se MISTURAM. A frase que fica vermelha na secao 7 e "...e o homonimo NAO devolve a do
///    vizinho -- B na celula de A: 1". Sao dois planetas de verdade, e um deles responde com a
///    bancada do outro.
///
///  * **O POUSO CALCULA OUTRA CHAVE** (`PousarEmProcedural` com `destino.Seed ^ 1`) -> **35 OK, 3
///    falhas, e as tres nas secoes 6 e 7**. As cinco primeiras ficam INTEIRAS no verde: a obra e
///    gravada certa, volta certa do disco, o crivo separa as zonas direitinho -- e o jogador nunca
///    mais acha a bancada dele, porque quem chega pousa noutra zona. E o modo de falha que uma
///    bancada de chave inventada nao tem como enxergar, e a razao de as secoes 6 e 7 existirem.
///
/// UMA NOTA SOBRE A MIGRACAO, porque a intuicao erra aqui: matar o `set` do `Obra.ZonaDoDiscoAntigo`
/// (**35 OK, 3 falhas**) NAO derruba "as construcoes de ontem NAO SOMEM na carga". Elas continuam
/// tres no `_noChao` -- so que com `ZonaNome` vazio. Elas nao somem da LISTA, somem do MUNDO, e por
/// isso quem reprova sao as checagens de zona ("a da Terra volta na Terra pre-feita", "a carga SABE
/// que converteu", "com as zonas convertidas intactas"). Contar obras nunca teria pego este defeito.
/// =====================================================================================================
///
///     Godot --headless -- --server --obrateste
/// </summary>
public partial class GameServer
{
	private int _obtOk, _obtFalhou;

	private void AfirmarObra(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _obtOk++; GD.Print($"[obra]   OK    {oque}"); return; }
		_obtFalhou++;
		GD.PrintErr($"[obra]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDasObras()
	{
		_obtOk = _obtFalhou = 0;
		GD.Print("[obra] ================ A CHAVE DE ZONA DAS CONSTRUCOES ================");

		// A FOTO DO MUNDO DE VERDADE. Esta bancada roda no boot, com o `_noChao` ja povoado pela
		// mobilia do mapa -- mexer nele sem devolver deixaria o servidor sem banco e sem bancada.
		var reais = new List<Obra>(_noChao);
		int idReal = _proximaObraId;

		string arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus-obrateste.json");

		// O CORPO DAS SECOES 6 e 7. Nasce nulo porque as cinco primeiras nao precisam de ninguem no
		// mundo -- e um corpo forjado que ficasse de pe durante elas apareceria nas listas de zona.
		ServerPlayer? cobaia = null;

		try
		{
			_noChao.Clear();

			// =====================================================================
			// 1. A MIGRACAO: O `mundo.json` DE ONTEM
			// =====================================================================
			// ============================ O JSON E ESCRITO A MAO, E TEM QUE SER ============================
			// Em toda outra bancada deste projeto isso seria o erro classico ("testar a copia"). Aqui e o
			// contrario: o formato antigo e a ENTRADA sob teste, e **nenhum codigo vivo consegue mais
			// produzi-lo**. Escrever os bytes de ontem e a unica forma de provar que a carga de hoje os
			// entende -- e no dia em que ela parar de entender, esta secao cai.
			// =========================================================================================
			// AS TRES OBRAS SAO OS TRES CASOS QUE O DISCO DE ONTEM PODIA TER, e nao tres copias da
			// mesma: a da TERRA (nome de zona pre-feita, o caso feliz), a da NAVE (o nome ambiguo --
			// todo interior se chamava "Nave") e a de um mundo SORTEADO (o caso que o formato antigo
			// nao tinha como representar). A terceira e a que diz a verdade sobre o alcance desta
			// porta: ela reproduz o comportamento de ontem, ela nao RESSUSCITA o que ontem ja perdia.
			System.IO.File.WriteAllText(arq, """
			[
			  {
			    "Id": 7,
			    "Tipo": "Research_Station",
			    "Zona": "Earth",
			    "X": 640,
			    "Y": 480,
			    "DonoConta": "velho",
			    "DonoNome": "Bulma",
			    "Aparafusada": true,
			    "DoMapa": false,
			    "Lab": 0,
			    "Vida": 8,
			    "ArmaduraMax": 900,
			    "Armadura": 900,
			    "ErguidaEm": 123
			  },
			  {
			    "Id": 9,
			    "Tipo": "Gravity_Machine",
			    "Zona": "Nave",
			    "X": 64,
			    "Y": 64,
			    "DonoConta": "velho",
			    "DonoNome": "Bulma",
			    "Aparafusada": false,
			    "DoMapa": false,
			    "Lab": 0,
			    "Vida": 8,
			    "ArmaduraMax": 1200,
			    "Armadura": 1200,
			    "ErguidaEm": 456
			  },
			  {
			    "Id": 41,
			    "Tipo": "Research_Station",
			    "Zona": "Deserto-120",
			    "X": 900,
			    "Y": 900,
			    "DonoConta": "velho",
			    "DonoNome": "Vegeta",
			    "Aparafusada": false,
			    "DoMapa": false,
			    "Lab": 0,
			    "Vida": 8,
			    "ArmaduraMax": 300,
			    "Armadura": 300,
			    "ErguidaEm": 789
			  }
			]
			""");

			CarregarMundoDeDisco(arq);

			AfirmarObra("as construcoes de ontem NAO SOMEM na carga", _noChao.Count == 3, $"{_noChao.Count}");
			if (_noChao.Count == 3)
			{
				Obra velha = _noChao[0];
				AfirmarObra("  ...a da Terra volta na Terra pre-feita (nome puro -> ZoneKey.Premade)",
							velha.Zona.Equals(ZoneKey.Premade("Earth")), velha.Zona.ToString());
				AfirmarObra("  ...e a carga SABE que converteu (o log conta)",
							_noChao.TrueForAll(o => o.Migrada));
				AfirmarObra("  ...com o resto dela intacto",
							velha.Id == 7 && velha.Tipo == "Research_Station"
							&& velha.DonoNome == "Bulma" && Math.Abs(velha.ArmaduraMax - 900) < 0.001,
							$"#{velha.Id} {velha.Tipo} {velha.DonoNome} {velha.ArmaduraMax}");

				// O PROXIMO ID SAI DO MAIOR QUE VOLTOU, e nao da CONTAGEM. Com tres obras e o id 41 no
				// arquivo, contar daria 4 -- e a proxima construcao erguida nasceria com um id que ja
				// existe, dois moveis respondendo ao mesmo clique e um deles sumindo na primeira
				// gravacao. E o tipo de estrago que so aparece semanas depois, no mundo de alguem.
				AfirmarObra("  ...e o proximo id passa do MAIOR que voltou (nao da contagem)",
							_proximaObraId == 42, $"{_proximaObraId}");

				// A HONESTIDADE DA PORTA. A obra que estava num mundo sorteado volta PRE-FEITA, porque e
				// isso e so isso que o arquivo de ontem sabia dizer. Afirmar aqui que ela volta pro
				// planeta gerado seria afirmar que a migracao inventa uma seed que ninguem gravou.
				Obra doSorteado = _noChao[2];
				AfirmarObra("  ...e a que estava num mundo SORTEADO volta pre-feita -- a porta reproduz "
							+ "ontem, nao ressuscita o que ontem ja perdia",
							doSorteado.ZonaTipo == ZoneKey.KindPremade && doSorteado.ZonaSeed == 0,
							$"tipo {doSorteado.ZonaTipo} seed {doSorteado.ZonaSeed}");

				// ---- A PORTA DISPARA UMA VEZ SO ----
				// A frase escrita no `Obra.ZonaDoDiscoAntigo` e "na primeira gravacao o campo velho
				// desaparece do arquivo sozinho, e esta porta passa a nunca mais disparar". Isto e a
				// frase virando checagem: grava o que acabou de ser migrado e RELE.
				GravarMundoEm(arq);
				_noChao.Clear();
				CarregarMundoDeDisco(arq);

				AfirmarObra("  ...regravado e relido, nada se perde", _noChao.Count == 3, $"{_noChao.Count}");
				AfirmarObra("  ...e a porta da migracao NAO dispara de novo",
							_noChao.TrueForAll(o => !o.Migrada));
				AfirmarObra("  ...com as zonas convertidas intactas",
							_noChao.Count == 3 && _noChao[0].Zona.Equals(ZoneKey.Premade("Earth"))
							&& _noChao[1].Zona.Equals(ZoneKey.Premade("Nave")));
			}

			// =====================================================================
			// 2. O PLANETA GERADO SOBREVIVE AO DISCO
			// =====================================================================
			// O defeito antigo em uma linha: esta obra voltaria como `Premade("Deserto-120")`, uma zona
			// que nao existe em manifesto nenhum -- ela some do mundo sem uma palavra.
			_noChao.Clear();
			ZoneKey gerado = ZoneKey.Procedural("Deserto-120", 0xDEADBEEF);
			var nova = new Obra { Id = 42, Tipo = "Gravity_Machine", X = 100, Y = 200, DonoNome = "Vegeta" };
			nova.PorZona(gerado);
			_noChao.Add(nova);

			GravarMundoEm(arq);
			_noChao.Clear();
			CarregarMundoDeDisco(arq);

			AfirmarObra("a obra de um planeta GERADO volta do disco", _noChao.Count == 1, $"{_noChao.Count}");
			if (_noChao.Count == 1)
			{
				AfirmarObra("  ...na MESMA zona (tipo procedural + nome + seed)",
							_noChao[0].Zona.Equals(gerado), _noChao[0].Zona.ToString());
				AfirmarObra("  ...e ela NAO virou pre-feita",
							_noChao[0].ZonaTipo == ZoneKey.KindProcedural, $"tipo {_noChao[0].ZonaTipo}");
				AfirmarObra("  ...e nao se anuncia migrada (formato novo nao dispara a porta antiga)",
							!_noChao[0].Migrada);
			}

			// =====================================================================
			// 3. DOIS HOMONIMOS NAO DIVIDEM OBRA
			// =====================================================================
			// ============================ QUEM RESPONDE E CODIGO DE PRODUCAO ============================
			// A pergunta "que obra esta nesta celula desta zona?" tem UM dono no servidor -- o
			// `ObraNaCelula`, que o soco usa pra achar o que derrubar. Reescrever o filtro aqui mediria a
			// reescrita: e exatamente assim que um crivo quebrado passa numa bancada verde.
			// =======================================================================================
			_noChao.Clear();
			ZoneKey a = ZoneKey.Procedural("Deserto-120", 111);   // seria `[1:-2]k0`
			ZoneKey b = ZoneKey.Procedural("Deserto-120", 222);   // seria `[1:2]k0` -- MESMO NOME
			var oa = new Obra { Id = 1, Tipo = "Research_Station", X = 400, Y = 400 };
			var ob = new Obra { Id = 2, Tipo = "Research_Station", X = 400, Y = 400 };
			oa.PorZona(a);
			ob.PorZona(b);
			_noChao.Add(oa);
			_noChao.Add(ob);

			(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(400, 400);
			AfirmarObra("a celula do mundo A devolve a obra de A (e nao a de B)",
						ObraNaCelula(a, cx, cy)?.Id == 1, $"{ObraNaCelula(a, cx, cy)?.Id}");
			AfirmarObra("a celula do mundo B devolve a obra de B",
						ObraNaCelula(b, cx, cy)?.Id == 2, $"{ObraNaCelula(b, cx, cy)?.Id}");
			AfirmarObra("e a mesma celula da TERRA nao devolve nenhuma das duas",
						ObraNaCelula(ZoneKey.Premade("Earth"), cx, cy) == null);

			// E O DISCO GUARDA AS DUAS SEPARADAS -- a metade que o crivo em memoria nao prova.
			GravarMundoEm(arq);
			_noChao.Clear();
			CarregarMundoDeDisco(arq);
			AfirmarObra("depois do reboot as duas continuam em mundos diferentes",
						_noChao.Count == 2 && !_noChao[0].Zona.Equals(_noChao[1].Zona),
						$"{_noChao.Count} obra(s)");

			// =====================================================================
			// 4. O CAMPO MORTO NAO FICA NO ARQUIVO
			// =====================================================================
			// DUAS ARMADILHAS DE UMA VEZ. A primeira: a porta da migracao se chama "Zona" no JSON, e um
			// campo de compatibilidade que continua sendo GRAVADO nunca deixa de existir -- ele vira
			// verdade paralela pra alguem ler daqui a um ano. A segunda: a `ZoneKey` montada e uma
			// propriedade publica, e sem `[JsonIgnore]` ela seria gravada AO LADO das partes, com dois
			// caminhos pra mesma resposta.
			string texto = System.IO.File.ReadAllText(arq);
			AfirmarObra("o arquivo novo nao escreve mais o campo antigo",
						!texto.Contains("\"Zona\":", StringComparison.Ordinal),
						texto.Length > 300 ? texto[..300] : texto);
			AfirmarObra("...e escreve as TRES partes",
						texto.Contains("\"ZonaTipo\"", StringComparison.Ordinal)
						&& texto.Contains("\"ZonaNome\"", StringComparison.Ordinal)
						&& texto.Contains("\"ZonaSeed\"", StringComparison.Ordinal));
			AfirmarObra("...e nao guarda o bit de migracao (ele e da carga, nao do mundo)",
						!texto.Contains("Migrada", StringComparison.Ordinal));

			// =====================================================================
			// 5. O INTERIOR DE NAVE JA E DISTINGUIVEL
			// =====================================================================
			// O outro sintoma da chave por nome: TODO interior de nave se chama "Nave". A construcao
			// dentro dele continua RECUSADA (ver `Posicionar`), mas agora por causa do ciclo de vida da
			// nave e nao por causa da chave -- e esta secao e o que prova que a chave nao e mais desculpa.
			_noChao.Clear();
			var d7 = new Obra { Id = 1, Tipo = "Research_Station", X = 64, Y = 64 };
			var d8 = new Obra { Id = 2, Tipo = "Research_Station", X = 64, Y = 64 };
			d7.PorZona(Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(7));
			d8.PorZona(Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(8));
			_noChao.Add(d7);
			_noChao.Add(d8);

			(int ix, int iy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(64, 64);
			AfirmarObra("o interior da nave #7 e o da #8 sao zonas DIFERENTES",
						ObraNaCelula(Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(7), ix, iy)?.Id == 1
						&& ObraNaCelula(Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(8), ix, iy)?.Id == 2);

			// =====================================================================
			// 6 e 7. O UNIVERSO DE VERDADE
			// =====================================================================
			cobaia = ForjarCobaiaDeObra();
			OMundoSorteadoDeVerdade(cobaia, arq);
			OsHomonimosDeVerdade(cobaia, arq);
		}
		catch (Exception e)
		{
			// SEM ISTO, UM ESTOURO NA SECAO 6 LEVA A BANCADA INTEIRA JUNTO e o `finally` devolve o
			// mundo sem que ninguem saiba por que o placar sumiu. Mesma disciplina da `--cargoportas`.
			AfirmarObra($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// O CORPO SAI DO MUNDO ANTES DE QUALQUER OUTRA COISA: ele esta numa lista de zona e no
			// `_players`, e um corpo forjado esquecido la aparece no snapshot de quem entrar depois.
			if (cobaia != null)
			{
				ZoneList(cobaia.Zone.Hash).Remove(cobaia);
				_players.Remove(cobaia.Id);
				_avisadosDeEspera.Remove(cobaia.Id);
				_avisadosDePlanetaMorto.Remove(cobaia.Id);
			}

			_noChao.Clear();
			_noChao.AddRange(reais);
			_proximaObraId = idReal;
			try { if (System.IO.File.Exists(arq)) System.IO.File.Delete(arq); } catch { /* temp */ }
		}

		// A BANCADA DEVOLVE O MUNDO COMO PEGOU -- e isto e checagem, e nao confianca no `finally`.
		//
		// Ela roda no BOOT, antes das outras: um corpo forjado esquecido no `_players` ou uma obra a
		// mais no `_noChao` nao quebram esta bancada, quebram a PROXIMA -- e o rastro aponta pra
		// bancada errada. As secoes 6 e 7 sao as primeiras daqui a por corpo no mundo, entao a conta
		// passou a valer a pena.
		AfirmarObra("a bancada devolve o mundo como pegou (nenhum corpo e nenhuma obra a mais)",
					!_players.ContainsKey(IdDaCobaiaDeObra) && _noChao.Count == reais.Count
					&& _proximaObraId == idReal,
					$"corpo={_players.ContainsKey(IdDaCobaiaDeObra)} obras={_noChao.Count}/{reais.Count}");

		GD.Print($"[obra] ================ {_obtOk} OK, {_obtFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// O CORPO E O CAMINHO ATE UM MUNDO SORTEADO
	// =====================================================================
	/// <summary>Id longe de qualquer jogador de verdade -- mesma faixa das outras bancadas.</summary>
	private const int IdDaCobaiaDeObra = 90_800;

	/// <summary>
	/// O CORPO QUE CONSTROI. Nasce no espaco de proposito: e de la que se chega a um mundo sorteado,
	/// e por-lo direto na superficie seria pular justamente o trecho sob teste.
	///
	/// Tecnologia e zeni sao dados na mao porque o que esta em julgamento nao e o preco da bancada --
	/// e a zona em que ela cai. Sem eles o `construir` recusaria e as duas secoes mediriam a recusa.
	/// </summary>
	private ServerPlayer ForjarCobaiaDeObra()
	{
		var novo = new ServerPlayer
		{
			Id = IdDaCobaiaDeObra,
			Peer = null,
			Name = "bancada: o construtor",
			Race = "Human",
			Genero = "Male",
			Idade = 30,
			Zone = ZonaDoEspaco,
			Pos = new Vec2(0, 0),
			Conta = "bancada_obra_1",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1_000_000 },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.techskill = 60;
		novo.Ficha.Zeni = 1_000_000;
		return novo;
	}

	/// <summary>
	/// POUSA DE VERDADE. Devolve false quando o corpo continuou em orbita.
	///
	/// ============================ POR QUE O TIQUE ENTRA UMA VEZ SO ============================
	/// A primeira chamada e o `TickDoEspaco`, e ela e a que importa: e ela que faz a pergunta de
	/// producao *"que planeta ha sob este ponto?"* (`Espaco.PlanetaSob`) e encomenda o mundo. Dali em
	/// diante o laco chama o `PousarEmProcedural` direto -- o MESMO metodo que o tique chamaria, sem o
	/// resto do tique junto. O resto do tique inclui o SOL, e quatrocentas voltas de dano solar
	/// matariam a cobaia antes de o terreno ficar pronto: a bancada estaria medindo a estrela.
	///
	/// O `TickDasGeracoes` no laco nao e cortesia: o terreno nasce numa thread do pool e **so o tique
	/// publica o resultado** (ver o cabecalho dele). Sem esta linha o mundo fica pronto e ninguem
	/// colhe, e o pouso espera pra sempre.
	/// ======================================================================================
	/// </summary>
	private bool PousarDeVerdade(ServerPlayer pl, PlanetaNoEspaco destino)
	{
		MoveToZone(pl.Id, ZonaDoEspaco, destino.Pos);
		pl.ChunkAtual = ChunkId.De(pl.Pos);

		TickDoEspaco(pl);

		for (int i = 0; i < 400 && Espaco.EhEspaco(pl.Zone); i++)
		{
			TickDasGeracoes();
			PousarEmProcedural(pl, destino);
			if (Espaco.EhEspaco(pl.Zone)) System.Threading.Thread.Sleep(5);
		}

		return !Espaco.EhEspaco(pl.Zone);
	}

	/// <summary>Ergue uma bancada de pesquisa nos pes do corpo, pelo verbo. Nulo = nao subiu.</summary>
	private Obra? ErguerPeloVerbo(ServerPlayer pl)
	{
		ComandoDeTech(pl, "construir", "Research_Station");
		ComandoDeTech(pl, "posicionar", $"Research_Station/{pl.Pos.X:0}/{pl.Pos.Y:0}");
		return _noChao.LastOrDefault(o => string.Equals(o.DonoConta, pl.Conta, StringComparison.Ordinal));
	}

	// =====================================================================
	// 6. O UNIVERSO DE VERDADE: POUSAR, ERGUER, REINICIAR, REENCONTRAR
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO ESTA SECAO ENXERGA ============================
	/// As secoes 2 e 3 provam que a `Obra` GUARDA a chave certa. Elas nao provam a unica coisa que o
	/// jogador percebe: que ele volta e a bancada dele esta la. Entre guardar e reencontrar ha tres
	/// pecas que nenhuma chave inventada exercita -- o pouso (que monta a chave do corpo celeste), o
	/// verbo (que copia a zona do corpo pra obra) e a re-enumeracao do universo depois do boot (que
	/// precisa devolver o MESMO planeta a partir da seed, sem nada guardado).
	///
	/// Por isso o reinicio aqui e de verdade: o `_noChao` e esvaziado, **o mundo gerado e descarregado
	/// do cache** e o corpo volta pro espaco. O planeta nasce do zero uma segunda vez e o corpo desce
	/// nele pelo funil de sempre. So entao a pergunta e feita -- e quem responde e o `ObraNaCelula`,
	/// que e o crivo que o soco usa pra achar o que derrubar.
	/// ==================================================================================
	/// </summary>
	private void OMundoSorteadoDeVerdade(ServerPlayer pl, string arq)
	{
		if (AcharPlanetaGerado(SeedDoUniverso) is not { } p)
		{
			AfirmarObra("ha um mundo SORTEADO no universo de producao pra erguer uma bancada", false,
						"nenhum procedural nas chunks varridas");
			return;
		}

		AfirmarObra($"(montagem) '{p.Nome}' e um mundo sorteado, vivo, no universo de producao",
					!p.Premade && !PlanetaMorto(p), $"{p.Nome} premade={p.Premade}");

		_noChao.Clear();
		_proximaObraId = 1;

		AfirmarObra($"o corpo POUSA em {p.Nome} pelo funil de producao (PlanetaSob -> PousarEmProcedural)",
					PousarDeVerdade(pl, p), $"zona: {pl.Zone}");
		if (Espaco.EhEspaco(pl.Zone)) return;

		ZoneKey ondeErgueu = pl.Zone;
		AfirmarObra("  ...e a zona do pouso e PROCEDURAL, com o nome e a seed do corpo celeste",
					EhZonaProcedural(ondeErgueu) && ondeErgueu.Name == p.Nome && ondeErgueu.Seed == p.Seed,
					ondeErgueu.ToString());

		Obra? erguida = ErguerPeloVerbo(pl);
		AfirmarObra("a bancada sobe pelo VERBO de producao (construir + posicionar)",
					erguida != null, $"{_noChao.Count} obra(s) no chao");
		if (erguida == null) return;

		int id = erguida.Id;
		(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(erguida.X, erguida.Y);
		AfirmarObra("  ...e ela nasce com a zona do CORPO, e nao com o nome dele",
					erguida.Zona.Equals(ondeErgueu) && erguida.ZonaSeed == p.Seed, erguida.Zona.ToString());

		// ---- O REINICIO ----
		GravarMundoEm(arq);
		_noChao.Clear();
		// O MUNDO TAMBEM MORRE NO BOOT. Sem esta linha o segundo pouso acharia o terreno ainda no
		// cache e a secao nao teria medido a re-enumeracao -- que e metade do que ela existe pra medir.
		_zonasGeradas.Remove(ondeErgueu.Hash);
		CarregarMundoDeDisco(arq);

		AfirmarObra("depois do reinicio a obra do mundo sorteado volta do disco",
					_noChao.Count == 1, $"{_noChao.Count}");

		// E O CORPO VOLTA PELO ESPACO: o planeta e reenumerado da seed do universo e o terreno nasce
		// de novo. Nada disto le o `mundo.json` -- sao dois caminhos que tem que se encontrar sozinhos.
		bool voltou = PousarDeVerdade(pl, p);
		AfirmarObra("o corpo pousa DE NOVO no mesmo mundo (a chave do pouso e funcao pura da seed)",
					voltou && pl.Zone.Equals(ondeErgueu), pl.Zone.ToString());

		AfirmarObra("**E A BANCADA ESTA LA, NA CELULA CERTA** -- e quem responde e o crivo do soco",
					ObraNaCelula(pl.Zone, cx, cy)?.Id == id,
					$"celula ({cx},{cy}) devolveu {ObraNaCelula(pl.Zone, cx, cy)?.Id.ToString() ?? "nada"}");
	}

	// =====================================================================
	// 7. OS HOMONIMOS DE VERDADE
	// =====================================================================
	/// <summary>
	/// ============================ SEM ESTA SECAO, A CHAVE NOVA PASSA POR SORTE ============================
	/// A secao 3 monta dois `ZoneKey.Procedural("Deserto-120", 111|222)` e prova que o crivo os separa.
	/// O que ela NAO pode provar e que o par existe: se o universo de producao nunca gerasse dois
	/// mundos de mesmo nome, a chave por nome teria sido suficiente o tempo todo e o trabalho inteiro
	/// seria uma resposta pra uma pergunta que ninguem fez. Pior -- se o par existir mas com outra
	/// forma (nomes que colidem por outro motivo), a secao 3 continuaria verde medindo o caso errado.
	///
	/// Entao aqui o par nao e afirmado: e ACHADO, varrendo as celulas de sistema do universo de
	/// producao com o gerador de producao. E o cabecalho do `Obra.ZonaTipo` afirma um par concreto
	/// ("Deserto-120" saindo de `[1:-2]k0` e de `[1:2]k0`); a varredura prefere justamente esse nome,
	/// entao o dia em que a afirmacao do comentario deixar de ser verdade, o log desta secao mostra
	/// qual par ela achou no lugar.
	/// ================================================================================================
	/// </summary>
	private void OsHomonimosDeVerdade(ServerPlayer pl, string arq)
	{
		if (AcharHomonimosDeVerdade() is not { } par)
		{
			AfirmarObra("o universo de producao TEM dois mundos sorteados de mesmo nome", false,
						"nenhuma colisao de nome nas celulas varridas");
			return;
		}

		GD.Print($"[obra] o par homonimo: '{par.A.Nome}' na celula [{par.CelA.Sx}:{par.CelA.Sy}] "
				 + $"(seed {par.A.Seed}) e na celula [{par.CelB.Sx}:{par.CelB.Sy}] (seed {par.B.Seed})");

		AfirmarObra($"o universo de producao TEM dois mundos sorteados chamados '{par.A.Nome}'",
					string.Equals(par.A.Nome, par.B.Nome, StringComparison.Ordinal) && par.A.Seed != par.B.Seed,
					$"{par.A.Nome}#{par.A.Seed} x {par.B.Nome}#{par.B.Seed}");
		AfirmarObra("  ...e a chave DE ONTEM (so o nome) nao tinha como distingui-los",
					ZoneKey.Premade(par.A.Nome).Equals(ZoneKey.Premade(par.B.Nome)));
		AfirmarObra("  ...enquanto a de hoje distingue",
					!ZoneKey.Procedural(par.A.Nome, par.A.Seed)
						 .Equals(ZoneKey.Procedural(par.B.Nome, par.B.Seed)));

		_noChao.Clear();
		_proximaObraId = 1;

		// ---- UMA BANCADA EM CADA UM, PELO CAMINHO DE SEMPRE ----
		if (!PousarDeVerdade(pl, par.A)) { AfirmarObra($"o corpo pousa no primeiro '{par.A.Nome}'", false, $"{pl.Zone}"); return; }
		ZoneKey zonaA = pl.Zone;
		Obra? oa = ErguerPeloVerbo(pl);

		if (!PousarDeVerdade(pl, par.B)) { AfirmarObra($"o corpo pousa no segundo '{par.B.Nome}'", false, $"{pl.Zone}"); return; }
		ZoneKey zonaB = pl.Zone;
		Obra? ob = ErguerPeloVerbo(pl);

		AfirmarObra("uma bancada sobe em cada um dos dois mundos homonimos",
					oa != null && ob != null && oa.Id != ob.Id, $"{_noChao.Count} obra(s)");
		if (oa == null || ob == null) return;

		AfirmarObra("  ...e as duas zonas so diferem na SEED (o nome e o mesmo)",
					zonaA.Name == zonaB.Name && zonaA.Seed != zonaB.Seed, $"{zonaA} x {zonaB}");

		(int ax, int ay) = Jandirus.Core.Tech.CatalogoDeObras.Celula(oa.X, oa.Y);
		(int bx, int by) = Jandirus.Core.Tech.CatalogoDeObras.Celula(ob.X, ob.Y);

		AfirmarObra("cada mundo devolve a SUA bancada (crivo de producao)",
					ObraNaCelula(zonaA, ax, ay)?.Id == oa.Id && ObraNaCelula(zonaB, bx, by)?.Id == ob.Id);
		AfirmarObra("...e o homonimo NAO devolve a do vizinho",
					ObraNaCelula(zonaB, ax, ay)?.Id != oa.Id && ObraNaCelula(zonaA, bx, by)?.Id != ob.Id,
					$"B na celula de A: {ObraNaCelula(zonaB, ax, ay)?.Id.ToString() ?? "nada"}");

		// ---- E O DISCO ----
		GravarMundoEm(arq);
		_noChao.Clear();
		CarregarMundoDeDisco(arq);

		AfirmarObra("depois do reinicio as duas voltam, cada uma na sua",
					_noChao.Count == 2 && ObraNaCelula(zonaA, ax, ay)?.Id == oa.Id
					&& ObraNaCelula(zonaB, bx, by)?.Id == ob.Id, $"{_noChao.Count} obra(s)");

		// A ULTIMA E A QUE FECHA O CIRCUITO: a chave que voltou do disco tem que bater com a que o
		// UNIVERSO calcula hoje pra aquele corpo celeste, re-enumerado da celula, sem nada guardado.
		AfirmarObra("...e a chave de cada uma bate com a que o universo reenumera da celula",
					_noChao.Exists(o => o.Zona.Equals(ChaveReenumerada(SeedDoUniverso, par.CelA, par.A.Seed)))
					&& _noChao.Exists(o => o.Zona.Equals(ChaveReenumerada(SeedDoUniverso, par.CelB, par.B.Seed))));
	}

	/// <summary>
	/// A chave de um corpo celeste, calculada DE NOVO a partir da celula -- e o que o cliente e o
	/// pouso fazem depois de um boot, quando nao ha nada carregado alem da seed do universo.
	///
	/// A SEMENTE VEM POR PARAMETRO, e e a que o servidor esta rodando: a afirmacao e "a chave que
	/// voltou do disco bate com a que ESTE universo reenumera", e ela so vale se as duas pontas
	/// olharem o mesmo universo.
	/// </summary>
	private static ZoneKey ChaveReenumerada(ulong semente, SistemaId cel, ulong seedDoCorpo)
	{
		if (Sistemas.Do(semente, cel.Sx, cel.Sy) is { } s)
			foreach (PlanetaNoEspaco q in s.Planetas())
				if (q.Seed == seedDoCorpo) return ZoneKey.Procedural(q.Nome, q.Seed);

		return default;
	}

	/// <summary>
	/// DOIS MUNDOS SORTEADOS DE MESMO NOME, achados no universo de producao.
	///
	/// A varredura e por CELULA DE SISTEMA e nao por chunk: o nome vem de
	/// `{bioma}-{|Sx|%1000}{|Sy|%1000}{k}` (`SistemaSolar.Planeta`), ou seja e a celula que decide o
	/// nome -- varrer chunks olharia a mesma celula dezenas de vezes.
	///
	/// DUAS ARMADILHAS DENTRO DO LACO:
	///   * o MESMO corpo aparece varias vezes numa varredura larga (celulas vizinhas devolvem o mesmo
	///     sistema quando o raio alcanca); dois nomes iguais com a MESMA seed nao sao homonimos, sao o
	///     mesmo planeta, e aceitar isso daria um par falso que passaria verde pra sempre;
	///   * planeta MORTO nao aceita pouso (`TickDoEspaco` recusa), entao um par com um cadaver dentro
	///     travaria a secao inteira num "o corpo nao pousou" que nao tem nada a ver com a chave.
	/// </summary>
	private (PlanetaNoEspaco A, SistemaId CelA, PlanetaNoEspaco B, SistemaId CelB)? AcharHomonimosDeVerdade(
		string preferido = "Deserto-120", int alcance = 40)
	{
		var vistos = new Dictionary<string, (PlanetaNoEspaco P, SistemaId C)>(StringComparer.Ordinal);
		(PlanetaNoEspaco A, SistemaId CelA, PlanetaNoEspaco B, SistemaId CelB)? primeiro = null;

		for (int sy = -alcance; sy <= alcance; sy++)
			for (int sx = -alcance; sx <= alcance; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				var cel = new SistemaId(sx, sy);

				foreach (PlanetaNoEspaco p in s.Planetas())
				{
					if (p.Premade || PlanetaMorto(p)) continue;
					if (!vistos.TryGetValue(p.Nome, out (PlanetaNoEspaco P, SistemaId C) antes))
					{ vistos[p.Nome] = (p, cel); continue; }

					if (antes.P.Seed == p.Seed) continue;   // o mesmo corpo, visto duas vezes

					if (string.Equals(p.Nome, preferido, StringComparison.Ordinal))
						return (antes.P, antes.C, p, cel);

					primeiro ??= (antes.P, antes.C, p, cel);
				}
			}

		return primeiro;
	}
}
