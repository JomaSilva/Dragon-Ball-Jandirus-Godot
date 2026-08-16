using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA VIVA DA CIDADE E DO BANCO -- `--cidadeteste`.
///
/// ============================ O QUE ELA MEDE QUE A DE FORA NAO ALCANCA ============================
/// A bancada `cidade` do AssetPipeline le os ARQUIVOS: `.col`, `.pedacos`, `.objetos`. Ela prova que
/// o disco esta certo, e isso e metade -- a outra metade e o servidor de pe, e ele diverge do disco
/// em coisas que so existem depois do boot:
///
///   * a colisao das MAQUINAS nao esta so no `.col`: `AplicarColisaoDasObras` bloqueia por cima, em
///     runtime, a partir do `_noChao`. Uma construcao que suma da lista continua bloqueando no
///     arquivo e para de bloquear no jogo -- e o arquivo continuaria verde;
///   * o ALARME da carga e `GD.PushError` dentro do `CarregarTech`. Ele nao existe em arquivo
///     nenhum, e ate hoje ninguem nunca conferiu que ele dispara;
///   * o ALCANCE DE USO e uma conta do servidor sobre a posicao do CORPO, e nao sobre a celula.
///
/// ============================ AS QUATRO FAMILIAS ============================
///  1. O ALARME DA CARGA -- construcao que bloqueia sem arte tem que gritar. Provado pelo caminho de
///     PRODUCAO (`AvisarDasMudas`), com uma construcao muda INJETADA pelo parser de verdade.
///  2. A CIDADE ESTA DE PE -- as maquinas que o construtor promete existem como `Obra` em Vegeta.
///  3. A RECIPROCA VIVA -- toda obra densa BLOQUEIA de verdade no mapa da zona, e toda obra de pe
///     tem arte (senao o cliente pinta a reserva cinza e o jogador ve um caixote).
///  4. O BANCO SERVE -- um corpo longe e RECUSADO, o mesmo corpo perto e ACEITO, e o zeni se move.
///
/// CADA UMA REPROVA COM O DEFEITO INJETADO, e as injecoes estao ao lado de cada familia.
/// =============================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA -- MEDIDO ============================
/// O placar limpo e **23 OK, 0 FALHAS**. Os tres defeitos abaixo foram postos no codigo de PRODUCAO,
/// um por vez, com o servidor subindo de verdade na porta 7984:
///
///  * **O ALARME CEGO** (`CatalogoDeObras.SemDesenho` devolvendo vazio) -> **22 OK, 1 FALHA**:
///    "o alarme de PRODUCAO grita pela construcao muda: gritou 0 vez(es)". So a familia 1 cai, e cai
///    pela injecao. Note o que isso prova: a bancada nao tem laco proprio -- ela chama o
///    `AvisarDasMudas` da carga. Apagar aquele trecho do `CarregarTech` fica vermelho AQUI.
///
///  * **A CAMADA DE COLISAO MUDA** (`AplicarColisaoDasObras` deixando de chamar `Bloquear`) ->
///    **22 OK, 1 FALHA**: "uma obra densa ERGUIDA em (240,240) passa a bloquear: antes=False
///    depois=False". As TREZE maquinas do mapa continuam bloqueando e a familia continuaria verde
///    sem a obra erguida -- porque a densidade delas foi assada no `.col`. Foi exatamente isso que
///    a primeira versao desta secao mediu errado, e o vermelho dela e que ensinou a diferenca.
///
///  * **O BANCO ENGOLIDO NA CARGA** (`CarregarObjetosDoMapa` pulando o `Bank`) -> **13 OK, 4
///    FALHAS**. E o defeito de verdade, reencenado no caminho de verdade: cai a travessia
///    (`13/14`), cai "ha um banco", cai a injecao que dependia dele, e a familia 4 inteira nao chega
///    a rodar. Dez checagens somem de uma vez -- a marca de um objeto que deixou de existir, e nao
///    de um que ficou errado.
/// ============================================================================================
///
///     Godot --headless -- --server --rede 7984 --cidadeteste
/// </summary>
public sealed partial class GameServer
{
	private int _cidOk, _cidFalhou;

	private void AfirmarCidade(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _cidOk++; GD.Print($"[cidade]   OK    {oque}"); return; }
		_cidFalhou++;
		GD.PrintErr($"[cidade]   FALHA {oque}   {detalhe}");
	}

	/// <summary>A zona da cidade -- a mesma constante do carimbo do pipeline (`CidadeDeVegeta.Z`).</summary>
	private static ZoneKey ZonaDaCidade => ZoneKey.Premade("Vegeta");

	public void RodarBancadaDaCidade()
	{
		_cidOk = 0; _cidFalhou = 0;
		GD.Print("\n===== BANCADA DA CIDADE (--cidadeteste) =====\n");

		CidAlarme();
		CidDePe();
		CidReciproca();
		CidBanco();
		CidRegenerador();

		GD.Print($"\n[cidade] ===== {_cidOk} OK, {_cidFalhou} FALHA(S) =====\n");
	}

	// =====================================================================
	// 1. O ALARME DA CARGA
	// =====================================================================
	/// <summary>
	/// O DEFEITO E INJETADO PELO PARSER, e nao montado na mao.
	///
	/// O que o servidor le no boot e TEXTO. Um `new Construcao { Densa = true, Arte = "" }` provaria
	/// que o `SemDesenho` filtra uma lista -- e nao provaria nada sobre o caminho por onde o dado
	/// realmente chega, que e justamente onde um campo se perde calado. Aqui a construcao muda entra
	/// pelo `construcoes.json` de verdade, acrescentada ao texto e re-lida.
	/// </summary>
	private void CidAlarme()
	{
		GD.Print("--- 1. O ALARME: construcao que bloqueia sem arte tem que GRITAR ---");

		if (_obras == null) { AfirmarCidade("o catalogo de construcoes carregou", false); return; }

		int mudasDeVerdade = AvisarDasMudas(_obras);
		AfirmarCidade("o catalogo publicado nao tem construcao densa e muda", mudasDeVerdade == 0,
					  $"{mudasDeVerdade}");
		GD.Print($"        {_obras.Total} construcoes | {_obras.Todas.Count(c => c.Densa)} bloqueiam");

		const string cj = "res://Assets/Data/construcoes.json";
		if (!Godot.FileAccess.FileExists(cj)) { AfirmarCidade("o construcoes.json esta no lugar", false); return; }

		string texto = Godot.FileAccess.GetFileAsString(cj);
		const string muda = "{ \"id\": \"TESTE_Parede_Fantasma\", \"nome\": \"parede fantasma\", "
						  + "\"desc\": \"injetada pela bancada\", \"custo\": 1, \"tech\": 0, \"racas\": [], "
						  + "\"arte\": \"\", \"estado\": \"\", \"densa\": 1, \"px\": 0, \"py\": 0, "
						  + "\"tipo\": \"/obj/TESTE/ParedeFantasma\" }";
		int fim = texto.LastIndexOf(']');
		string sujo = fim < 0 ? texto : texto[..fim].TrimEnd().TrimEnd(',') + ",\n  " + muda + "\n]";

		CatalogoDeObras injetado = CatalogoDeObras.Parse(sujo);
		AfirmarCidade("  ...e a injecao entrou mesmo no catalogo (o parser a leu)",
					  injetado.Get("TESTE_Parede_Fantasma") is { Densa: true, Arte.Length: 0 },
					  $"{injetado.Total} vs {_obras.Total}");

		// A CHAMADA E A DE PRODUCAO. O que se mede aqui e o alarme do `CarregarTech`, e nao uma
		// copia dele: se alguem apagar aquele trecho da carga, esta linha fica vermelha.
		int gritou = AvisarDasMudas(injetado);
		AfirmarCidade("[injecao] o alarme de PRODUCAO grita pela construcao muda",
					  gritou == 1, $"gritou {gritou} vez(es)");
		GD.Print("        (o PushError acima, com 'TESTE_Parede_Fantasma', E a prova -- ele veio da injecao)");

		// ...e a MESMA construcao com arte nao pode gritar, senao o alarme e so um contador
		int comArte = AvisarDasMudas(CatalogoDeObras.Parse(
			sujo.Replace("\"arte\": \"\"", "\"arte\": \"res://Assets/Sprites/Misc/x.tres\"")));
		AfirmarCidade("[injecao] ...e nao grita pela mesma construcao COM arte", comArte == 0, $"{comArte}");
		GD.Print("");
	}

	// =====================================================================
	// 2. A CIDADE ESTA DE PE
	// =====================================================================
	/// <summary>
	/// AS MAQUINAS DA CIDADE VIRARAM `Obra`.
	///
	/// A conta nao e escrita aqui: ela vem do `.objetos` da zona, que e o que o
	/// <see cref="CarregarObjetosDoMapa"/> le. O que esta familia cobra e a TRAVESSIA -- toda linha
	/// do arquivo virou uma construcao de pe, com o tipo certo e na celula certa. Contar so o total
	/// deixaria passar dez bancadas viradas em dez bancos.
	/// </summary>
	private void CidDePe()
	{
		GD.Print("--- 2. A CIDADE ESTA DE PE (as maquinas do mapa viraram Obra) ---");

		ZoneEntry? e = _catalogo?.Get(ZonaDaCidade);
		if (e == null || e.Objetos.Length == 0 || !Godot.FileAccess.FileExists(e.Objetos))
		{
			AfirmarCidade("Vegeta tem `.objetos`", false);
			return;
		}

		List<ObjetoDoMapa> noArquivo = ObjetosDoMapa.Parse(Godot.FileAccess.GetFileAsString(e.Objetos));
		List<Obra> dePe = [.. _noChao.Where(o => o.Zona.Equals(ZonaDaCidade))];

		const int t = ZoneCollision.TileSize;
		int casaram = noArquivo.Count(a => dePe.Any(o =>
			o.Tipo == a.Id && CatalogoDeObras.Celula(o.X, o.Y) == (a.X, a.Y)));

		GD.Print($"        {noArquivo.Count} no `.objetos` | {dePe.Count} de pe em Vegeta");
		foreach (IGrouping<string, ObjetoDoMapa> g in noArquivo.GroupBy(a => a.Id).OrderBy(g => g.Key))
			GD.Print($"        {g.Key,-22} {g.Count(),3}x  ->  de pe: {dePe.Count(o => o.Tipo == g.Key)}");

		AfirmarCidade("TODA linha do `.objetos` virou uma obra de pe, no tipo e na celula certos",
					  casaram == noArquivo.Count, $"{casaram}/{noArquivo.Count}");
		AfirmarCidade("  ...e todas nasceram aparafusadas e marcadas como do mapa",
					  dePe.Where(o => o.DoMapa).All(o => o.Aparafusada),
					  $"{dePe.Count(o => o.DoMapa && !o.Aparafusada)} soltas");

		// AS BANCADAS E O REGENERADOR sao o que o construtor do DM promete. A conta vem do arquivo,
		// nao daqui -- ver o cabecalho. O que se afirma e que os dois tipos existem em Vegeta.
		AfirmarCidade("  ...ha bancada de pesquisa em Vegeta (o lab do construtor)",
					  dePe.Any(o => o.Tipo == "Research_Station"),
					  $"{dePe.Count(o => o.Tipo == "Research_Station")}");
		AfirmarCidade("  ...e ha um regenerador", dePe.Any(o => o.Tipo == "Regenerator"));
		AfirmarCidade("  ...e ha um banco", dePe.Any(o => o.Tipo == "Bank"));

		// [INJECAO] uma linha do arquivo que nao vira obra: e o defeito exato do `objeto ??=` que
		// engolia o banco de Vegeta -- o `.dmm` tinha, o `.objetos` nao recebia, e nada reclamava.
		ObjetoDoMapa sumida = noArquivo[0];
		int semEla = noArquivo.Count(a => !a.Equals(sumida)
			&& dePe.Any(o => o.Tipo == a.Id && CatalogoDeObras.Celula(o.X, o.Y) == (a.X, a.Y)));
		AfirmarCidade($"[injecao] apagar '{sumida.Id}' ({sumida.X},{sumida.Y}) da conta REPROVA a travessia",
					  semEla == noArquivo.Count - 1, $"{semEla}");
		_ = t;
		GD.Print("");
	}

	// =====================================================================
	// 3. A RECIPROCA VIVA
	// =====================================================================
	/// <summary>
	/// TODA OBRA DENSA BLOQUEIA DE VERDADE, e toda obra de pe tem desenho.
	///
	/// ============================ POR QUE O MAPA VIVO E NAO O `.col` ============================
	/// Porque sao dois mapas. O `.col` traz o que o conversor assou; `AplicarColisaoDasObras` poe uma
	/// camada de runtime por cima a cada boot e a cada construcao erguida. As duas concordam hoje em
	/// Vegeta porque a maquina do mapa esta nas duas -- e essa concordancia e o que precisa ser
	/// MEDIDO, e nao suposto: a mesma Research Station ja bloqueou vinda do `.dmm` e nao bloqueou
	/// construida por um jogador, e o dono achou isso atravessando o banco.
	/// ==========================================================================================
	/// </summary>
	private void CidReciproca()
	{
		GD.Print("--- 3. A RECIPROCA VIVA: obra densa bloqueia, obra de pe tem arte ---");

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZonaDaCidade);
		if (mapa == null || _obras == null) { AfirmarCidade("o mapa vivo de Vegeta existe", false); return; }

		List<Obra> densas = [.. _noChao.Where(o => o.Zona.Equals(ZonaDaCidade)
											   && _obras.Get(o.Tipo) is { Densa: true })];
		int naoBloqueiam = densas.Count(o =>
		{
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			return !mapa.BlockedCell(cx, cy);
		});
		AfirmarCidade($"as {densas.Count} obras densas de Vegeta BLOQUEIAM no mapa vivo",
					  naoBloqueiam == 0, $"{naoBloqueiam} atravessaveis");

		// A OUTRA PONTA: obra de pe sem arte vira o caixote cinza do `ObraDesenhada`. Nao e parede
		// invisivel -- e pior de diagnosticar, porque o jogador ve UMA COISA e ela nao e nada.
		List<Obra> semArte = [.. _noChao.Where(o => _obras.Get(o.Tipo) is { Arte.Length: 0 })];
		AfirmarCidade("nenhuma obra de pe no mundo inteiro esta sem arte",
					  semArte.Count == 0, string.Join(", ", semArte.Select(o => o.Tipo).Distinct()));

		// ============================ A MAQUINA DO MAPA BLOQUEIA PELO `.col`, E ISTO A BANCADA APRENDEU APANHANDO ============================
		// A primeira versao desta injecao apagava a camada de runtime (`LimparObras`) e cobrava que as
		// obras densas parassem de bloquear. Ela ficou VERMELHA com o codigo certo: as treze
		// continuaram bloqueando, porque a densidade de uma maquina que veio do `.dmm` ja foi ASSADA
		// no `.col` pelo conversor -- a camada de runtime e redundante pra elas.
		//
		// A camada de runtime so decide sozinha no caso que importa e que ja quebrou uma vez: a obra
		// que um JOGADOR ergue. Ela nao existe em arquivo nenhum. Entao a injecao passa a erguer uma,
		// que e exatamente o par que o dono achou -- "a mesma Research Station bloqueia vinda do mapa
		// e nao bloqueia construida".
		// ======================================================================================================================
		(int bx, int by) = CelulaLivrePerto(mapa, 240, 240);
		var erguida = new Obra
		{
			Id = 990101,
			Tipo = "Research_Station",
			X = bx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
			Y = by * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY,
			DonoNome = "bancada_cidade",
			Aparafusada = true,
		};
		erguida.PorZona(ZonaDaCidade);

		bool antes = mapa.BlockedCell(bx, by);
		_noChao.Add(erguida);
		AplicarColisaoDasObras(ZonaDaCidade);
		bool depois = mapa.BlockedCell(bx, by);
		AfirmarCidade($"uma obra densa ERGUIDA em ({bx},{by}) passa a bloquear (a camada de runtime)",
					  !antes && depois, $"antes={antes} depois={depois}");

		mapa.LimparObras();
		bool semCamada = mapa.BlockedCell(bx, by);
		AfirmarCidade("[injecao] sem a camada de runtime ela PARA de bloquear -- e a diferenca entre "
					  + "a maquina do mapa e a construida", !semCamada, "continuou bloqueando");

		_noChao.Remove(erguida);
		AplicarColisaoDasObras(ZonaDaCidade);
		AfirmarCidade("  ...e derrubada, a celula fica livre de novo", !mapa.BlockedCell(bx, by));

		int voltou = densas.Count(o =>
		{
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			return !mapa.BlockedCell(cx, cy);
		});
		AfirmarCidade("  ...com as 13 do mapa intactas", voltou == 0, $"{voltou}");
		GD.Print("");
	}

	/// <summary>Uma celula livre a partir de (x,y) -- a injecao precisa de chao, nao de parede.</summary>
	private static (int X, int Y) CelulaLivrePerto(ZoneCollision mapa, int x, int y)
	{
		for (int r = 0; r < 60; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
					if (!mapa.BlockedCell(x + dx, y + dy)) return (x + dx, y + dy);
		return (x, y);
	}

	// =====================================================================
	// 4. O BANCO SERVE
	// =====================================================================
	/// <summary>
	/// UM CORPO DE VERDADE CHEGA NO BANCO E MEXE NELE.
	///
	/// ============================ POR QUE ATE O ZENI TEM QUE SE MOVER ============================
	/// Porque o banco de Vegeta ja esteve em TODOS os estados que enganam uma bancada mais curta:
	/// ele existe no `.dmm` e nao chegava ao `.objetos`; a logica dele esta inteira em
	/// `GameServer.Banco.cs` e havia ZERO bancos no mundo; e ele tem `custo: -1`, ou seja, ninguem
	/// fabrica um pra repor. "Esta na lista" nao responde nenhuma dessas.
	///
	/// Entao a familia faz o que o jogador faz: chega perto, pede o extrato, deposita, e confere o
	/// numero. E faz a recusa TAMBEM -- de longe o servidor tem que dizer nao, senao o alcance nao
	/// existe e a familia inteira ficaria verde com o banco no outro lado do planeta.
	/// ==========================================================================================
	/// </summary>
	private void CidBanco()
	{
		GD.Print("--- 4. O BANCO SERVE: um corpo chega, pede o extrato e deposita ---");

		Obra? banco = _noChao.FirstOrDefault(o => o.Zona.Equals(ZonaDaCidade) && o.Tipo == "Bank");
		if (banco == null) { AfirmarCidade("ha um banco de pe em Vegeta", false); return; }

		const float t = ZoneCollision.TileSize;
		var corpo = new ServerPlayer
		{
			Id = 990001,
			Peer = null,
			Name = "bancada_cidade",
			Race = "Saiyan",
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaCidade,
			Pos = new Vec2(banco.X, banco.Y),
			Conta = "bancada_cidade",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 100 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		corpo.Ficha.Class = "Normal";
		PorNoMundo(corpo);

		List<string>? escutaAntes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		try
		{
			// --- LONGE: dez tiles ao sul. O servidor tem que recusar. ---
			corpo.Pos = new Vec2(banco.X, banco.Y + 10 * t);
			EscutaDeAvisos.Clear();
			bool tratou = ComandoDeBanco(corpo, "banco_ver", "");
			AfirmarCidade("de longe o verbo do banco e RECUSADO",
						  tratou && EscutaDeAvisos.Any(m => m.Contains("precisa estar num banco")),
						  string.Join(" | ", EscutaDeAvisos));

			// --- PERTO: uma celula ao sul, dentro do `Interacoes.Alcance` ---
			corpo.Pos = new Vec2(banco.X, banco.Y + t);
			EscutaDeAvisos.Clear();
			corpo.Ficha.Zeni = 1234;
			corpo.Ficha.ZeniBanco = 0;

			ComandoDeBanco(corpo, "banco_ver", "");
			AfirmarCidade("de perto o extrato SAI", EscutaDeAvisos.Any(m => m.Contains("--- banco ---")),
						  string.Join(" | ", EscutaDeAvisos));

			EscutaDeAvisos.Clear();
			ComandoDeBanco(corpo, "banco_depositar", "");
			AfirmarCidade("...e depositar MOVE o zeni de verdade",
						  corpo.Ficha.ZeniBanco == 1234 && corpo.Ficha.Zeni == 0,
						  $"bolso={corpo.Ficha.Zeni} cofre={corpo.Ficha.ZeniBanco}");

			EscutaDeAvisos.Clear();
			ComandoDeBanco(corpo, "banco_sacar", "");
			AfirmarCidade("...e sacar traz de volta",
						  corpo.Ficha.Zeni == 1234 && corpo.Ficha.ZeniBanco == 0,
						  $"bolso={corpo.Ficha.Zeni} cofre={corpo.Ficha.ZeniBanco}");

			// --- A BEIRADA: e ela que prova que o alcance e um numero e nao um desejo ---
			// Um pixel ALEM do alcance tem que recusar; um pixel aquem tem que aceitar. Testar so
			// "perto aceita, longe recusa" ficaria verde com qualquer raio, inclusive o planeta todo.
			foreach ((string caso, float dy, bool esperado) in new (string, float, bool)[]
			{
				("um pixel AQUEM do alcance", Interacoes.Alcance - 1, true),
				("um pixel ALEM do alcance ", Interacoes.Alcance + 1, false),
			})
			{
				corpo.Pos = new Vec2(banco.X, banco.Y + dy);
				bool aceita = CaixaPerto(corpo) != null;
				AfirmarCidade($"{caso}: {(aceita ? "aceita" : "recusa")}", aceita == esperado);
			}

			// [INJECAO] o banco deixa de estar de pe: e o estado em que Vegeta esteve ate ontem --
			// o `.dmm` tinha o banco, o `.objetos` nao, e o servidor subia sem nenhum.
			_noChao.Remove(banco);
			corpo.Pos = new Vec2(banco.X, banco.Y + t);
			EscutaDeAvisos.Clear();
			ComandoDeBanco(corpo, "banco_ver", "");
			AfirmarCidade("[injecao] sem o banco de pe, o mesmo corpo no mesmo lugar e RECUSADO",
						  EscutaDeAvisos.Any(m => m.Contains("precisa estar num banco")),
						  string.Join(" | ", EscutaDeAvisos));
			_noChao.Add(banco);
		}
		finally
		{
			EscutaDeAvisos = escutaAntes;
			_players.Remove(corpo.Id);
			ZoneList(corpo.Zone.Hash).Remove(corpo);
		}
		GD.Print("");
	}

	// =====================================================================
	// 5. O REGENERADOR E A SAIDA DE QUEM NAO TEM RACA PRA ISSO
	// =====================================================================
	/// <summary>
	/// ============================ O TANQUE DEVOLVE MEMBRO -- E ELE E A UNICA SAIDA DO HUMANO ============================
	/// O dono: *"um MEMBRO QUEBRADO deveria DEMORAR BASTANTE pra regenerar sozinho, **sem ajuda como
	/// uma MAQUINA DE REGENERACAO** ou algo do tipo"*. A frase tem duas metades, e a segunda e um
	/// sistema que precisava existir: com a cura passiva virando privilegio de raca, um Humano ou um
	/// Saiyajin que perdesse um braco **nao tinha caminho de volta nenhum** -- `Body.Curar` pula
	/// membro decepado, a passiva nao devolve membro sem `canheallopped`, e a ativa e do Namekuseijin.
	/// Era morrer ou ficar manco pra sempre.
	///
	/// **ESTA FAMILIA MORA NA CIDADE POR UM MOTIVO CONCRETO**: o regenerador de Vegeta ja esta de pe
	/// e APARAFUSADO, posto la pelo construtor de mapa -- e a familia 2 acima ja afirma que ele
	/// existe. Montar um `new Obra` aqui mediria o meu proprio objeto; assim se mede a maquina que o
	/// jogador encontra.
	///
	/// **E O TIQUE E O DE PRODUCAO** (`TickDasMaquinasDeCura`), varrendo `_players` inteiro: chamar a
	/// conta na mao provaria que a formula existe, e nao que o laco alcanca o corpo deitado no tanque.
	/// O `dt` e grande de proposito -- e argumento, nao relogio de parede.
	/// ==============================================================================================================
	/// </summary>
	private void CidRegenerador()
	{
		GD.Print("--- 5. O REGENERADOR devolve o membro perdido (a saida de quem nao regenera) ---");

		Obra? tanque = _noChao.FirstOrDefault(o => o.Zona.Equals(ZonaDaCidade) && o.Tipo == "Regenerator");
		if (tanque == null) { AfirmarCidade("ha um regenerador de pe em Vegeta", false); return; }

		var corpo = new ServerPlayer
		{
			Id = 990002,
			Peer = null,
			Name = "bancada_tanque",
			Race = "Human",           // sem `fastRegen` e sem `canheallopped`: o caso do pedido
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaCidade,
			Pos = new Vec2(tanque.X, tanque.Y),
			Conta = "bancada_tanque",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 100 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		corpo.Ficha.Class = "Normal";
		PorNoMundo(corpo);

		try
		{
			// O BRACO SAI PELO FUNIL (`Decepar`), que leva a mao junto -- e nao escrevendo `Decepado`.
			Jandirus.Core.Combat.BodyPart braco = corpo.Combate.Corpo.Achar("Braco esquerdo")!;
			corpo.Combate.Corpo.Decepar(braco);
			corpo.Combate.SincronizarVida();

			AfirmarCidade("PRECONDICAO: o corpo esta sem o braco (e a mao foi junto)",
						  braco.Decepado && corpo.Combate.Corpo.Achar("Mao esquerda")!.Decepado);

			// --- LONGE DO TANQUE: dez tiles. Nao pode acontecer nada, nem depois de meia hora. ---
			corpo.Pos = new Vec2(tanque.X, tanque.Y + 10 * ZoneCollision.TileSize);
			for (int i = 0; i < 60; i++) TickDasMaquinasDeCura(30);   // 30 min
			AfirmarCidade("longe do tanque o braco NAO volta, nem em meia hora", braco.Decepado);

			// --- EM CIMA DELE ---
			corpo.Pos = new Vec2(tanque.X, tanque.Y);

			// ANTES DA HORA: 4 minutos nao bastam. Sem esta metade, um tanque instantaneo passaria.
			for (int i = 0; i < 24; i++) TickDasMaquinasDeCura(10);   // 240 s
			AfirmarCidade($"em cima do tanque, 240 s AINDA nao bastam (o preco e {SegundosDoRegeneradorPorMembro:0} s)",
						  braco.Decepado, $"voltou cedo demais");

			// E AGORA SIM: passa dos 300 s seguidos.
			for (int i = 0; i < 12; i++) TickDasMaquinasDeCura(10);   // +120 s
			AfirmarCidade("passados os 300 s deitado, o TANQUE devolve o braco",
						  !braco.Decepado, $"ainda decepado apos 360 s");
			AfirmarCidade("...e a MAO volta junto com ele (a cascata do `RegrowLimb`)",
						  !corpo.Combate.Corpo.Achar("Mao esquerda")!.Decepado);

			// ============================ E A CONTA NAO E UM DEPOSITO ============================
			// O defeito que esta linha existe pra pegar ja aconteceu neste arquivo: o relogio do
			// tanque era zerado num `else` que ficava DEPOIS de tres `continue`, entao sair de cima
			// nao zerava nada -- 299 s hoje mais 1 s amanha davam um membro. Aqui o corpo acumula
			// quase tudo, SAI, e volta: se o relogio fosse deposito, o membro voltaria na hora.
			// =================================================================================
			Jandirus.Core.Combat.BodyPart perna = corpo.Combate.Corpo.Achar("Perna direita")!;
			corpo.Combate.Corpo.Decepar(perna);
			corpo.Combate.SincronizarVida();

			for (int i = 0; i < 29; i++) TickDasMaquinasDeCura(10);   // 290 s em cima
			corpo.Pos = new Vec2(tanque.X, tanque.Y + 10 * ZoneCollision.TileSize);
			TickDasMaquinasDeCura(1);                                 // levantou e saiu
			corpo.Pos = new Vec2(tanque.X, tanque.Y);
			for (int i = 0; i < 3; i++) TickDasMaquinasDeCura(10);    // 30 s de volta

			AfirmarCidade("sair do tanque ZERA a conta -- 290 s + saida + 30 s NAO devolvem a perna",
						  perna.Decepado, "a espera virou deposito");
		}
		finally
		{
			_players.Remove(corpo.Id);
			ZoneList(corpo.Zone.Hash).Remove(corpo);
		}
		GD.Print("");
	}
}
