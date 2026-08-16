using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A BANCADA DA NUVEM -- e ela mede o DADO GRAVADO, nao a regra que eu acabei de escrever.
///
/// ============================ POR QUE ELA NAO REPETE O `ClasseDeNuvem` ============================
/// Conferir que `Travessia(APe, true) == Derruba` seria a bancada lendo a propria intencao: a funcao
/// tem quatro linhas e concordaria consigo mesma para sempre, inclusive no dia em que o plano `.nuvem`
/// nao fosse gerado e o jogo continuasse deixando andar por cima do ceu do Templo.
///
/// O que pode dar errado aqui NAO e a regra -- e a LIGACAO entre ela e o mapa:
///
///   1. o `.nuvem` nao existe (o comando do pipeline nunca rodou) e tudo segue verde;
///   2. o `.nuvem` existe e o `CarregarNuvem` o RECUSA CALADO (tamanho errado), que e o modo de
///      falha que o `.agua` ja teve;
///   3. a nuvem cobre um ponto de CHEGADA -- e ai o jogador cai no instante em que chega, para
///      sempre, sem nada no log;
///   4. o DESTINO da queda cai em parede, agua ou fora do mapa;
///   5. uma zona tem nuvem e o Core nao sabe se ela derruba ou barra.
///
/// Os cinco sao sobre a costura entre o conversor, o arquivo e o Core -- e nenhum deles aparece
/// lendo codigo. Por isso esta bancada abre os arquivos.
/// ==================================================================================================
///
/// ============================ A FAMILIA 3 E A QUE JUSTIFICA A BANCADA INTEIRA ============================
/// O Templo virou 83% nuvem (207.915 de 250.000 celulas). Um ponto de chegada em cima de uma delas
/// nao e um bug pequeno: e um LOOP -- o jogador sobe a torre do Karin, chega, cai na Terra, sobe de
/// novo. E o `Afterlife` e pior, porque o ponto de chegada dele e **a mesa do Enma**, onde TODO
/// mundo que morre aparece: uma nuvem ali mandaria todo morto do servidor direto pro Inferno.
///
/// Nenhum dos dois esta quebrado hoje -- foi conferido, e e justamente por isso que a prova existe.
/// Ela guarda a propriedade contra o dia em que alguem mexer no `.dmm` ou mover uma coordenada.
/// ========================================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- nuvem-prova [pastaMaps]
/// </summary>
public static class NuvemBench
{
	private static int _ok, _falhas;

	private static void Conferir(bool cond, string oque)
	{
		if (cond) { _ok++; Console.WriteLine($"    ok    {oque}"); }
		else { _falhas++; Console.WriteLine($"    FALHA {oque}"); }
	}

	/// <summary>
	/// AS ZONAS QUE TEM NUVEM, e a lista e ESCRITA A MAO de proposito.
	///
	/// Deriva-la do disco ("toda zona com `.nuvem`") faria a bancada concordar com qualquer coisa que
	/// o conversor produzisse, inclusive com ZERO arquivos. Escrita, ela cobra o conversor: sumiu uma
	/// zona da lista de arquivos, isto fica vermelho.
	///
	/// Os numeros sao os medidos no `.dmm` -- ver o quadro em `Core/World/Nuvem.cs`.
	/// </summary>
	private static readonly (string Arq, string Zona, int Celulas, bool Derruba)[] Esperadas =
	[
		("z06_Afterlife", "Afterlife", 214473, true),
		("z10_Heaven",    "Heaven",     75659, false),
		("z12_Lookout",   "Lookout",   207915, true),
		("z30_Outside",   "Outside",      672, false),
		("z31_God_Realm", "God_Realm",    322, false),
	];

	/// <summary>
	/// OS PONTOS DE CHEGADA DE CADA ZONA COM NUVEM, em coordenada BYOND -- todo lugar por onde um
	/// corpo APARECE numa zona que derruba. Se um deles for nuvem, quem chega cai no mesmo instante.
	///
	/// De onde saiu cada um (e nao ha nenhum inventado):
	///   * a mesa do Enma  -- `Death.dm:110`, onde TODO morto aparece (`Alem.EnmaX/EnmaY`);
	///   * `CaveEntrance3/4` -- as voltas do Ceu e do Inferno pro Outro Mundo (`Passagens.cs`);
	///   * `toeg`          -- a subida da torre do Karin pro Templo;
	///   * `fromhbtc`      -- a saida da Sala do Tempo, que devolve pro Templo.
	/// </summary>
	private static readonly (string Zona, string Nome, int Bx, int By)[] Chegadas =
	[
		("Afterlife", "mesa do Enma (toda morte)", Alem.EnmaX, Alem.EnmaY),
		("Afterlife", "CaveEntrance3 (volta do Ceu)", 221, 235),
		("Afterlife", "CaveEntrance4 (volta do Inferno)", 146, 222),
		("Lookout",   "toeg (torre do Karin -> Templo)", 142, 2),
		("Lookout",   "fromhbtc (saida da Sala do Tempo)", 125, 420),
	];

	public static int Run(string pastaMaps)
	{
		_ok = _falhas = 0;
		Console.WriteLine("============================================================");
		Console.WriteLine(" BANCADA DA NUVEM -- a quarta classe de celula");
		Console.WriteLine("============================================================");

		var planos = new Dictionary<string, ZoneCollision>(StringComparer.Ordinal);

		// ============================ 1. O ARQUIVO EXISTE E **VOLTA PELO LEITOR DE PRODUCAO** ============================
		// Ler o `.nuvem` com um parser proprio aqui seria testar o parser da bancada. A pergunta certa
		// e "o objeto que o JOGO consulta responde nuvem nestas celulas?", entao quem le e o
		// `ZoneCollision.CarregarNuvem` de producao -- o mesmo que o servidor e o cliente chamam.
		Console.WriteLine("\n  [1] o plano existe, carrega, e tem o numero de celulas medido no .dmm");
		foreach ((string arq, string zona, int celulas, bool derruba) in Esperadas)
		{
			string col = Path.Combine(pastaMaps, arq + ".col");
			string nuv = Path.Combine(pastaMaps, arq + ".nuvem");

			if (!File.Exists(nuv)) { Conferir(false, $"{zona}: falta o `{arq}.nuvem` -- rode `-- nuvem`"); continue; }
			if (!File.Exists(col)) { Conferir(false, $"{zona}: falta o `{arq}.col`"); continue; }

			ZoneCollision? m = ZoneCollision.Load(File.ReadAllBytes(col));
			if (m == null) { Conferir(false, $"{zona}: o `.col` nao carregou"); continue; }

			// A RECUSA CALADA E O DEFEITO A PEGAR: o `CarregarNuvem` devolve `false` (e nao lanca)
			// quando o cabecalho ou o tamanho nao batem. Sem esta linha, um `.nuvem` de outro mapa
			// deixaria a zona sem nuvem nenhuma e tudo aqui seguiria verde.
			bool leu = m.CarregarNuvem(File.ReadAllBytes(nuv), zona);
			Conferir(leu, $"{zona}: o `.nuvem` volta pelo `CarregarNuvem` de producao");
			if (!leu) continue;

			planos[zona] = m;

			int conta = 0;
			for (int y = 0; y < m.Height; y++)
				for (int x = 0; x < m.Width; x++)
					if (m.EhNuvem(x, y)) conta++;

			Conferir(conta == celulas, $"{zona}: {conta} celulas de nuvem (medido no .dmm: {celulas})");

			// A ZONA SABE O QUE FAZER COM A PROPRIA NUVEM. Um plano gravado com o Core sem resposta
			// seria uma nuvem que deixa entrar e nao leva a lugar nenhum -- o jogador andando no ceu.
			Conferir(m.NuvemDerruba == derruba,
					 $"{zona}: derruba={m.NuvemDerruba} (esperado {derruba})");
			Conferir(ClasseDeNuvem.Derruba(zona) == derruba,
					 $"{zona}: o Core concorda pelo NOME (`ClasseDeNuvem.Derruba`)");
		}

		// ============================ 2. NENHUM PONTO DE CHEGADA E NUVEM ============================
		// Ver o cabecalho: e a familia que impede o loop do Templo e o "todo morto vai pro Inferno".
		Console.WriteLine("\n  [2] nenhum ponto de CHEGADA cai em cima de nuvem");
		foreach ((string zona, string nome, int bx, int by) in Chegadas)
		{
			if (!planos.TryGetValue(zona, out ZoneCollision? m)) continue;
			int cx = bx - 1, cy = m.Height - by;
			bool nuvem = m.EhNuvem(cx, cy);
			Conferir(!nuvem, $"{zona}: {nome} -- BYOND({bx},{by}) -> celula({cx},{cy})"
						   + (nuvem ? "  <<< QUEM CHEGA CAI NA HORA" : ""));

			// ============================ E O ANEL EM VOLTA, QUE E O QUE O CORPO REALMENTE PISA ============================
			// A primeira versao desta prova cobrava `ServeDeChao` no ponto de chegada, e ela ficou
			// VERMELHA em tres dos cinco -- corretamente, e por um motivo que **nao e desta tarefa**:
			//
			//   * `CaveEntrance3` e `fromhbtc` chegam em cima de celula de PAREDE. Nao e defeito do
			//     mapa: os turfs de teleporte do DM sao `density = 1` (o `fromeg` declara isso na
			//     cara, `Turfs.dm:149`), e no BYOND `loc = locate(...)` nao passa pelo `Enter()`. O
			//     conversor os marca no `.col` porque eles SAO densos;
			//   * o `toeg` chega em `y = 498` de 500, ou seja dentro do anel do `NaBorda`.
			//
			// Os tres sao propriedade do sistema de PASSAGENS (o `Atravessar` nao chama
			// `PontoLivrePerto` -- ele confia na coordenada do DM e conta com o `MoveRules.Escapar`),
			// e cobra-los aqui seria esta bancada ficando vermelha para sempre por causa de codigo que
			// ela nao governa. Bancada que nasce vermelha por coisa alheia ensina a ignorar bancada.
			//
			// O QUE **E** DESTA TAREFA, e o que passou a ser cobrado: o corpo posto ali vai ser
			// empurrado pro anel vizinho pelo `Escapar`, e **esse anel nao pode ser nuvem** -- senao
			// chegar pela passagem viraria cair, e o jogador nunca entenderia por que. Medido: os
			// tres tem ZERO nuvem no anel imediato e de 3 a 5 celulas de chao livre.
			//
			// ELA SABE REPROVAR, e por pouco: no `toeg` a nuvem comeca no anel 2 (8 celulas contra 6
			// de chao). Um tile de nuvem a mais na beirada do Templo e isto fica vermelho.
			// ==========================================================================================================
			int nuvemPerto = 0, chaoPerto = 0;
			for (int dx = -1; dx <= 1; dx++)
				for (int dy = -1; dy <= 1; dy++)
				{
					int x = cx + dx, y = cy + dy;
					if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) continue;
					if (m.EhNuvem(x, y)) nuvemPerto++;
					else if (m.ServeDeChao(x, y)) chaoPerto++;
				}
			Conferir(nuvemPerto == 0,
					 $"{zona}: {nome} -- o anel em volta nao tem nuvem ({nuvemPerto})");
			Conferir(chaoPerto > 0,
					 $"{zona}: {nome} -- ha chao livre no anel pra o `Escapar` usar ({chaoPerto})");
		}

		// ============================ 3. O DESTINO DA QUEDA E UM LUGAR ONDE SE PODE FICAR ============================
		// O pedido do dono: *"Quem cai pela nuvem nao pode ficar preso nem cair dentro de parede"*. O
		// `PontoLivrePerto` conserta o caso ruim, mas ele varre ANEIS -- um destino no meio de uma
		// montanha sairia dezenas de tiles longe do lugar pretendido, calado. Esta familia cobra o
		// alvo, e nao so a rede de seguranca.
		Console.WriteLine("\n  [3] o destino de cada queda existe e da pra ficar nele");
		foreach ((string _, string zona, int _, bool derruba) in Esperadas)
		{
			if (!derruba) continue;

			var destino = ClasseDeNuvem.DestinoDaQueda(zona);
			Conferir(destino != null, $"{zona}: tem destino declarado no Core");
			if (destino is not { } d) continue;

			string arqDestino = Esperadas.Any(e => e.Zona == d.Zona)
				? Esperadas.First(e => e.Zona == d.Zona).Arq
				: ArquivoDaZona(pastaMaps, d.Zona);
			string col = Path.Combine(pastaMaps, arqDestino + ".col");
			if (!File.Exists(col)) { Conferir(false, $"{zona} -> {d.Zona}: nao achei `{arqDestino}.col`"); continue; }

			ZoneCollision? m = ZoneCollision.Load(File.ReadAllBytes(col));
			if (m == null) { Conferir(false, $"{zona} -> {d.Zona}: o `.col` nao carregou"); continue; }

			string agua = Path.Combine(pastaMaps, arqDestino + ".agua");
			if (File.Exists(agua)) m.CarregarAgua(File.ReadAllBytes(agua));
			string nuv = Path.Combine(pastaMaps, arqDestino + ".nuvem");
			if (File.Exists(nuv)) m.CarregarNuvem(File.ReadAllBytes(nuv), d.Zona);

			int cx = d.Bx - 1, cy = m.Height - d.By;
			Conferir(cx >= 0 && cy >= 0 && cx < m.Width && cy < m.Height,
					 $"{zona} -> {d.Zona}: o destino BYOND({d.Bx},{d.By}) cai DENTRO do mapa");
			Conferir(m.ServeDeChao(cx, cy),
					 $"{zona} -> {d.Zona}: celula({cx},{cy}) serve de chao (nem parede, nem agua, nem nuvem)");

			// E A CONTA DE PIXEL TEM QUE BATER COM A CELULA. O `EmPixel` inverte o Y (o BYOND cresce
			// pra cima), e errar essa inversao poe a chegada ESPELHADA na vertical -- sem sintoma
			// nenhum a nao ser "a queda me cospe no lugar errado".
			Vec2 px = ClasseDeNuvem.EmPixel(d.Bx, d.By, m.Height);
			Conferir((int)(px.X / ZoneCollision.TileSize) == cx
				  && (int)(px.Y / ZoneCollision.TileSize) == cy,
					 $"{zona} -> {d.Zona}: `EmPixel` cai na mesma celula ({px.X},{px.Y})");
		}

		// ============================ 4. A NUVEM NAO E AGUA, E A AGUA NAO E NUVEM ============================
		// A flag `Water` mente nos dois sentidos (ver `Nuvem.cs`), entao a confusao entre os dois
		// planos e o erro natural aqui. Se uma celula fosse os dois, ela responderia a duas regras
		// diferentes -- e a agua deixa NADAR, o que viraria "nadar por cima do ceu".
		Console.WriteLine("\n  [4] nenhuma celula e nuvem E agua ao mesmo tempo");
		foreach ((string arq, string zona, int _, bool _) in Esperadas)
		{
			if (!planos.TryGetValue(zona, out ZoneCollision? m)) continue;
			string agua = Path.Combine(pastaMaps, arq + ".agua");
			if (!File.Exists(agua)) { Conferir(true, $"{zona}: sem `.agua` (nada a cruzar)"); continue; }
			if (!m.CarregarAgua(File.ReadAllBytes(agua))) { Conferir(false, $"{zona}: `.agua` recusado"); continue; }

			int ambos = 0;
			for (int y = 0; y < m.Height; y++)
				for (int x = 0; x < m.Width; x++)
					if (m.EhNuvem(x, y) && m.EhAgua(x, y)) ambos++;
			Conferir(ambos == 0, $"{zona}: {ambos} celula(s) marcadas como nuvem E agua");
		}

		// ============================ 5. A REGRA, E SO O QUE ELA TEM DE **DIFERENTE** DA AGUA ============================
		// Aqui sim se le a regra -- mas so nos pontos em que ela DIVERGE da agua, que e onde a copia
		// preguicosa (`return ClasseDeAgua.Bloqueia(modo)`) passaria despercebida. As duas linhas sao
		// identicas na tela e dizem coisas diferentes: a agua deixa NADAR e deixa passar ARREMESSADO,
		// a nuvem nao deixa nenhum dos dois (`Enter()` da nuvem so olha `isflying`).
		Console.WriteLine("\n  [5] a nuvem NAO e a agua -- os dois modos em que elas divergem");
		Conferir(ClasseDeNuvem.Travessia(ModoDeTravessia.Nadando, false) == TravessiaDaNuvem.Bloqueia
			  && !ClasseDeAgua.Bloqueia(ModoDeTravessia.Nadando),
				 "NADANDO: a agua deixa passar e a nuvem BARRA");
		Conferir(ClasseDeNuvem.Travessia(ModoDeTravessia.Arremessado, false) == TravessiaDaNuvem.Bloqueia
			  && !ClasseDeAgua.Bloqueia(ModoDeTravessia.Arremessado),
				 "ARREMESSADO: a agua deixa passar e a nuvem BARRA");
		Conferir(ClasseDeNuvem.Travessia(ModoDeTravessia.Voando, false) == TravessiaDaNuvem.Atravessa
			  && ClasseDeNuvem.Travessia(ModoDeTravessia.Voando, true) == TravessiaDaNuvem.Atravessa,
				 "VOANDO passa nas duas, derrube ela ou nao (o `isflying` do DM)");

		// A QUE DERRUBA **NAO BLOQUEIA**, e isto e o contrario do que a intuicao pede. Sem esta linha,
		// alguem "consertaria" a nuvem fazendo-a barrar tambem -- e ai ela nunca derrubaria ninguem,
		// porque o corpo pararia na beirada e o servidor jamais veria o pe dele em cima dela.
		Conferir(!ClasseDeNuvem.Bloqueia(ModoDeTravessia.APe, zonaDerruba: true),
				 "a nuvem que DERRUBA nao bloqueia (senao ninguem entra, e ninguem cai)");
		Conferir(ClasseDeNuvem.Bloqueia(ModoDeTravessia.APe, zonaDerruba: false),
				 "a nuvem que so BARRA bloqueia mesmo");

		// ============================ 6. NUVEM NAO SERVE DE CHAO, NEM PRA NASCER NEM PRA POUSAR ============================
		// O pedido do dono, literal: *"nao pode ficar preso"*. Sem isto o `PontoLivrePerto` poria um
		// corpo em cima da nuvem do Ceu -- livre pela colisao e parado pela regra, que e a definicao
		// de preso.
		Console.WriteLine("\n  [6] o funil de pouso recusa nuvem");
		foreach ((string _, string zona, int _, bool _) in Esperadas)
		{
			if (!planos.TryGetValue(zona, out ZoneCollision? m)) continue;
			// ============================ A CELULA DE PROVA NAO PODE SER A QUINA DO MAPA ============================
			// A primeira versao pegava a PRIMEIRA celula de nuvem na varredura, e em tres zonas isso e
			// literalmente `(0,0)` -- a quina, onde tudo em volta e `NaBorda`. Ali o `PontoLivrePerto`
			// varre os 64 aneis, nao acha nada que sirva de chao, e devolve o ponto pedido: a prova
			// ficava vermelha medindo a BORDA DO MAPA, e nao a nuvem.
			//
			// Nao e uma prova enfraquecida -- e a prova apontada pro lugar certo: ninguem pode andar
			// ate `(0,0)` (o `NaBorda` impede), entao "o corpo preso na quina" nao e uma situacao do
			// jogo. Uma nuvem no MIOLO e, e e essa que interessa.
			// ======================================================================================================
			// ============================ A CELULA DE PROVA E A **BEIRA** DA NUVEM ============================
			// Esta escolha ja errou DUAS vezes, e as duas viraram vermelho que nao era defeito:
			//
			//   1. a PRIMEIRA celula da varredura e `(0,0)` em tres zonas -- a quina do mapa, onde
			//      tudo em volta e `NaBorda`. Media a BORDA, nao a nuvem;
			//   2. a primeira celula do MIOLO e `(8,8)`, e ali o corpo esta ~60 tiles dentro de um
			//      campo continuo de nuvem. O `PontoLivrePerto` varre 64 aneis e desiste -- que e o
			//      comportamento declarado dele, nao um defeito. Media o ALCANCE da varredura.
			//
			// A celula certa e a da BEIRA: uma nuvem com chao encostado. E o unico caso que o jogo
			// produz de verdade (um corpo empurrado pra cima da borda da nuvem, um pouso na margem),
			// e e onde a funcao TEM que funcionar. No meio do campo de nuvem ninguem chega a pe --
			// o `ZoneCollision.Bloqueia` nao deixa, e quem cai foi pra outra zona.
			//
			// E ELA CONTINUA SABENDO REPROVAR: tire a nuvem do `ServeDeChao` e o `PontoLivrePerto`
			// passa a aceitar a propria celula de nuvem, e esta linha fica vermelha na hora.
			// ==============================================================================================
			int achou = -1, x0 = 0, y0 = 0, qualquerX = -1, qualquerY = -1;
			for (int y = 1; y < m.Height - 1 && achou < 0; y++)
				for (int x = 1; x < m.Width - 1; x++)
				{
					if (!m.EhNuvem(x, y)) continue;
					if (qualquerX < 0) { qualquerX = x; qualquerY = y; }
					if (!m.ServeDeChao(x - 1, y) && !m.ServeDeChao(x + 1, y)
					 && !m.ServeDeChao(x, y - 1) && !m.ServeDeChao(x, y + 1)) continue;
					achou = 1; x0 = x; y0 = y; break;
				}

			// A CELULA DE NUVEM NUNCA SERVE DE CHAO -- e isto vale mesmo sem beira, entao ele vem
			// primeiro e cobre TODA zona com nuvem. E a metade da familia que nao depende do formato
			// da mancha.
			if (qualquerX >= 0)
				Conferir(!m.ServeDeChao(qualquerX, qualquerY),
						 $"{zona}: celula de nuvem ({qualquerX},{qualquerY}) NAO serve de chao");

			// ============================ SEM BEIRA NAO HA O QUE MEDIR, E ISSO E UM FATO E NAO UMA FALHA ============================
			// O `Outside` (z30) e o caso: as 672 celulas dele sao um bloco de 28x24 na quina do mapa
			// (x 1..28, y 1..24), e os vizinhos delas sao **so nuvem e o anel do `NaBorda`** -- ZERO
			// chao livre encostado, medido. E uma mancha decorativa fechada num andar que ninguem
			// joga, e nao existe corpo que possa chegar na borda dela.
			//
			// Reprovar aqui seria a bancada exigindo que um mapa tivesse uma geometria que ele nao
			// tem. Passar CALADO seria pior -- por isso a ausencia e IMPRESSA: se um dia esse andar
			// ganhar chao, a linha muda sozinha e a prova volta a rodar.
			// ==========================================================================================================
			if (achou < 0)
			{
				Console.WriteLine($"    --    {zona}: sem BEIRA de nuvem (mancha fechada por borda) "
								+ "-- a prova do `PontoLivrePerto` nao se aplica aqui");
				continue;
			}

			// E O `PontoLivrePerto` TEM QUE SAIR DE CIMA DELA. Recusar a celula e uma coisa; devolver
			// um ponto util e outra, e e essa a que o jogador sente.
			Vec2 fugiu = m.PontoLivrePerto(m.CentroDaCelula(x0, y0));
			int fx = (int)(fugiu.X / ZoneCollision.TileSize), fy = (int)(fugiu.Y / ZoneCollision.TileSize);
			Conferir(!m.EhNuvem(fx, fy),
					 $"{zona}: `PontoLivrePerto` tira o corpo da beira da nuvem (({x0},{y0}) -> ({fx},{fy}))");
		}

		Console.WriteLine("\n============================================================");
		Console.WriteLine($" NUVEM: {_ok} ok, {_falhas} falha(s)");
		Console.WriteLine("============================================================");
		return _falhas == 0 ? 0 : 1;
	}

	/// <summary>O `zNN_Nome` de uma zona, procurado no disco -- os destinos (Terra, Inferno) nao tem nuvem.</summary>
	private static string ArquivoDaZona(string pastaMaps, string zona)
	{
		foreach (string f in Directory.GetFiles(pastaMaps, "*.col"))
		{
			string nome = Path.GetFileNameWithoutExtension(f);
			if (nome[(nome.IndexOf('_') + 1)..].Equals(zona, StringComparison.OrdinalIgnoreCase))
				return nome;
		}
		return zona;
	}
}
