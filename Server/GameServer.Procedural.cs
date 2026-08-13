using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

public partial class GameServer
{
	/// <summary>
	/// Uma zona procedural que ALGUEM esta usando. Guarda o minimo: a ficha (pra gravidade e pro
	/// texto), a colisao (pra validar passo), o ponto de chegada e o planeta no mapa do universo
	/// (pra decolar de volta pro lugar certo).
	/// </summary>
	public sealed class ZonaGerada
	{
		public required MundoProcedural Ficha { get; init; }
		public required ZoneCollision Colisao { get; init; }
		public required Vec2 Chegada { get; init; }
		public required PlanetaNoEspaco NoEspaco { get; init; }

		/// <summary>O gerador teve de abrir uma clareira na marra (planeta sem chao livre).</summary>
		public required bool ClareiraEscavada { get; init; }

		/// <summary>
		/// A ASSINATURA DESTE MUNDO -- o mesmo numero que sai no log de <see cref="TickDasGeracoes"/>
		/// e que o cliente imprime ao pintar o planeta (`PlanetaProcedural.Nascer`).
		///
		/// GUARDADA E NAO RECALCULADA: ela ja foi paga na thread de fundo (sao 2,1 milhoes de passadas
		/// num mundo de 1000 -- ver o comentario em `GeracaoEmVoo.Tarefa`), e recalcula-la pra
		/// comparar poria de volta no tique exatamente o custo que se tirou dele. Oito bytes por mundo
		/// vivo, e com eles da pra PERGUNTAR ao servidor "que mundo voce gerou?" em vez de so ler o
		/// log com o olho -- que era o unico jeito de conferir as duas pontas ate agora.
		/// </summary>
		public required ulong Assinatura { get; init; }
	}

	/// <summary>
	/// Quantos mundos gerados ficam na memoria. Nao e um teto rigido: acima dele o servidor SO
	/// descarta os que estao VAZIOS. Se dez jogadores estiverem em dez planetas diferentes, os dez
	/// ficam -- descartar um mundo com gente dentro seria regerar a cada passo dela.
	/// </summary>
	private const int TetoDeZonasGeradas = 8;

	/// <summary>Hash da <see cref="ZoneKey"/> -> mundo gerado. Mesma chave que `_zones` usa.</summary>
	private readonly Dictionary<ulong, ZonaGerada> _zonasGeradas = [];

	/// <summary>
	/// UM MUNDO NASCENDO FORA DO TICK.
	///
	/// ============================ POR QUE ISTO NAO PODE SER SINCRONO ============================
	/// Gerar era feito aqui mesmo, dentro do tique que atende o pouso. Num mundo de 352 sao ~27 ms
	/// e cabia num tique de 33 ms -- por pouco, e era esse "por pouco" que fixava o teto de tamanho
	/// dos planetas. Nao era o desenho que limitava (esse ja e por pedaco, ver `PintorDePedacos`);
	/// era esta linha.
	///
	/// E o preco nao era de quem pousava: um tique parado e o servidor inteiro parado. Um jogador
	/// encostando num planeta novo fazia todo mundo online engasgar.
	///
	/// PODE SER OUTRA THREAD porque `GeradorDeTerreno.Gerar` e funcao pura: recebe parametros,
	/// devolve objeto novo, nao le nem escreve nada compartilhado (o Core nem conhece o engine). O
	/// UNICO ponto que precisa voltar pra thread principal e a escrita no cache, e e por isso que
	/// quem colhe e o <see cref="TickDasGeracoes"/> e nao uma continuacao da tarefa.
	/// ============================================================================================
	/// </summary>
	private sealed class GeracaoEmVoo
	{
		/// <summary>
		/// A tarefa devolve o mundo, quanto ele custou de CPU e a ASSINATURA dele.
		///
		/// O tempo de CPU vem junto porque sem ele so da pra medir o relogio de parede entre
		/// encomendar e colher, e ele mistura duas coisas muito diferentes: o tamanho do mundo e a
		/// demora em conseguir uma thread e o proximo tique. Confundir as duas leva a mexer no teto
		/// de tamanho quando o problema era fila.
		///
		/// A ASSINATURA VEM JUNTO PORQUE ELA NAO E BARATA: ela mistura byte a byte as duas grades
		/// de classe e o bitset de colisao -- num mundo de 1000 sao 2,1 milhoes de passadas, uns
		/// 20 ms. Calcula-la na hora de imprimir o log poria de volta no tique justamente uma parte
		/// do custo que se acabou de tirar dele, e por um diagnostico.
		/// </summary>
		public required System.Threading.Tasks.Task<(TerrenoGerado Terreno, double Ms, ulong Assinatura)> Tarefa { get; init; }
		public required MundoProcedural Ficha { get; init; }
		public required PlanetaNoEspaco NoEspaco { get; init; }
		public required ulong Inicio { get; init; }
	}

	private readonly Dictionary<ulong, GeracaoEmVoo> _gerando = [];

	/// <summary>Quem ja ouviu "o mundo esta se formando". Ver o porque em <see cref="PousarEmProcedural"/>.</summary>
	private readonly HashSet<int> _avisadosDeEspera = [];

	/// <summary>
	/// Esta zona e um planeta GERADO?
	///
	/// O espaco fica de fora de proposito: ele tambem e `KindProcedural` (ver `Espaco.Zona`), mas
	/// nao tem chao, nao tem colisao e nao se pousa nele -- ele E o lugar de onde se pousa.
	/// </summary>
	public static bool EhZonaProcedural(ZoneKey z) =>
		z.Kind == ZoneKey.KindProcedural && !Espaco.EhEspaco(z);

	/// <summary>
	/// O mundo desta zona, SE ele ja estiver pronto. Nulo tambem quer dizer "ainda nascendo".
	///
	/// Encomenda a geracao quando ela ainda nao comecou e quem pergunta sabe de que planeta se
	/// trata. Quem chama num laco (o pouso, que roda todo tique enquanto o corpo esta sobre o
	/// disco) so precisa tentar de novo: quando o mundo ficar pronto, esta funcao devolve.
	///
	/// <paramref name="noEspaco"/> so e usado quando a zona ainda nao existe: e o planeta do mapa
	/// do universo, guardado pra saber pra onde a decolagem devolve o corpo.
	/// </summary>
	public ZonaGerada? MundoDaZona(ZoneKey zona, PlanetaNoEspaco? noEspaco = null)
	{
		if (!EhZonaProcedural(zona)) return null;
		if (_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? viva)) return viva;
		if (noEspaco == null) return null;   // sem o planeta de origem nao da pra montar a ficha

		Encomendar(zona, noEspaco.Value);
		return null;
	}

	/// <summary>Poe um mundo pra nascer numa thread do pool. Repetir o pedido nao duplica trabalho.</summary>
	private void Encomendar(ZoneKey zona, PlanetaNoEspaco noEspaco)
	{
		if (_gerando.ContainsKey(zona.Hash)) return;

		MundoProcedural ficha = MundoProcedural.DaSeed(zona.Seed, zona.Name);

		// A FICHA E OS PARAMETROS SAO CALCULADOS AQUI, na thread principal, e a tarefa so recebe o
		// objeto pronto. Deixar a tarefa chamar `DaSeed` funcionaria hoje, mas amarraria a thread
		// de fundo a uma funcao que amanha pode querer ler estado do servidor.
		ParametrosDeTerreno p = ficha.Parametros();

		_gerando[zona.Hash] = new GeracaoEmVoo
		{
			Tarefa = System.Threading.Tasks.Task.Run(() =>
			{
				long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
				TerrenoGerado t = GeradorDeTerreno.Gerar(p);
				double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
				return (t, ms, t.Assinatura());
			}),
			Ficha = ficha,
			NoEspaco = noEspaco,
			Inicio = Time.GetTicksUsec(),
		};

		GD.Print($"[server] nascendo {ficha.Nome}: {ficha.Descricao()} (fora do tique)");
	}

	/// <summary>
	/// COLHE OS MUNDOS QUE FICARAM PRONTOS. Roda no tique, e e o unico lugar que escreve no cache.
	///
	/// A tarefa nao publica o resultado sozinha de proposito: uma continuacao rodaria na thread do
	/// pool e mexeria em `_zonasGeradas` e `_zones` enquanto o tique os le. Colher aqui custa uma
	/// varredura de um punhado de itens e dispensa qualquer trava.
	/// </summary>
	private void TickDasGeracoes()
	{
		if (_gerando.Count == 0) return;

		List<ulong>? prontas = null;
		foreach ((ulong hash, GeracaoEmVoo g) in _gerando)
			if (g.Tarefa.IsCompleted) (prontas ??= []).Add(hash);
		if (prontas == null) return;

		foreach (ulong hash in prontas)
		{
			GeracaoEmVoo g = _gerando[hash];
			_gerando.Remove(hash);

			if (g.Tarefa.IsFaulted)
			{
				GD.PushWarning($"[server] {g.Ficha.Nome} nao nasceu: {g.Tarefa.Exception?.GetBaseException().Message}");
				continue;
			}

			(TerrenoGerado t, double msDeCpu, ulong assinatura) = g.Tarefa.Result;
			PodarZonasVazias();

			_zonasGeradas[hash] = new ZonaGerada
			{
				Ficha = g.Ficha,
				Colisao = t.Colisao,
				Chegada = t.Spawn,
				NoEspaco = g.NoEspaco,
				ClareiraEscavada = t.ClareiraEscavada,
				Assinatura = assinatura,
			};

			// A ASSINATURA VAI NO LOG DE PROPOSITO. E o unico jeito barato de descobrir que cliente e
			// servidor divergiram: o numero daqui tem que bater com o que o cliente imprime ao entrar.
			//
			// OS DOIS TEMPOS. O de CPU e o custo do mundo (cresce com a area); o de espera e quanto
			// o jogador ficou em orbita, e inclui pegar uma thread e esperar o proximo tique. Ver
			// a nota em `GeracaoEmVoo.Tarefa` pro porque de nao bastar um so.
			GD.Print($"[server] gerado {g.Ficha.Nome}: {g.Ficha.Descricao()} "
					 + $"em {msDeCpu:0.0} ms de cpu, {(Time.GetTicksUsec() - g.Inicio) / 1000.0:0.0} ms de espera "
					 + $"| chegada ({t.SpawnCelX},{t.SpawnCelY}) | assinatura {assinatura:X16}"
					 + (t.ClareiraEscavada ? " | CLAREIRA ESCAVADA" : ""));
		}
	}

	/// <summary>
	/// Solta os mundos gerados em que nao ha mais ninguem.
	///
	/// So roda quando o cache passa do teto: varrer isto a cada pouso seria trabalho por nada, e
	/// um mundo ocioso custa ~15 KB -- o problema seria acumular centenas deles, nao ter oito.
	/// </summary>
	private void PodarZonasVazias()
	{
		if (_zonasGeradas.Count < TetoDeZonasGeradas) return;

		var mortas = new List<ulong>();
		foreach (ulong hash in _zonasGeradas.Keys)
			if (!_zones.TryGetValue(hash, out List<ServerPlayer>? gente) || gente.Count == 0)
				mortas.Add(hash);

		foreach (ulong hash in mortas) _zonasGeradas.Remove(hash);
		if (mortas.Count > 0) GD.Print($"[server] {mortas.Count} mundo(s) gerado(s) descarregado(s)");
	}

	/// <summary>
	/// A COLISAO DE QUALQUER ZONA -- pre-feita ou gerada.
	///
	/// Existe pra ser o unico ponto de consulta do validador de movimento: hoje ele le so o
	/// catalogo das zonas pre-feitas, e num planeta gerado isso devolve nulo, ou seja, o passo e
	/// validado so por VELOCIDADE e o jogador atravessa montanha. Ver o gancho no relatorio.
	/// </summary>
	public ZoneCollision? MapaDaZona(ZoneKey zona) =>
		EhZonaProcedural(zona)
			? (_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? v) ? v.Colisao : null)
			: _catalogo?.Get(zona)?.Mapa;

	/// <summary>
	/// A gravidade de uma zona gerada. Zero quando a zona nao e gerada (quem pergunta cai de volta
	/// no `planetas.json`, que e a fonte dos pre-feitos).
	/// </summary>
	public double GravidadeDaZonaGerada(ZoneKey zona) =>
		_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? v) ? v.Ficha.Gravidade : 0;

	/// <summary>
	/// O ponto de chegada de um mundo gerado que JA ESTA VIVO no servidor, ou nulo se ele nao esta.
	///
	/// Existe pro berco: quem morre no proprio planeta gerado nao precisa dar a volta pela orbita
	/// (`DestinoDoBerco`). Nulo nao e erro -- e "este mundo nao esta carregado", e a resposta certa
	/// e mandar o corpo pro espaco e deixar o `TickDoEspaco` pousar pelo caminho de verdade.
	/// </summary>
	public Vec2? ChegadaDaZonaGerada(ZoneKey zona) =>
		_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? v) ? v.Chegada : null;

	// =====================================================================
	// POUSAR E DECOLAR
	// =====================================================================
	/// <summary>
	/// POUSAR NUM PLANETA GERADO. Encostou, desce -- o mesmo "sem menu e sem porta" do pre-feito.
	///
	/// O corpo vai pro ponto que o proprio gerador garantiu livre (`TerrenoGerado.Spawn`), e nao
	/// pro `SpawnPos` da Terra: o centro de um mundo sorteado pode ser o meio de um lago ou de uma
	/// cordilheira, e nascer dentro de pedra e a primeira coisa que quebra num mapa gerado.
	/// </summary>
	public bool PousarEmProcedural(ServerPlayer pl, PlanetaNoEspaco destino)
	{
		var zona = ZoneKey.Procedural(destino.Nome, destino.Seed);
		ZonaGerada? viva = MundoDaZona(zona, destino);
		if (viva == null)
		{
			// O MUNDO ESTA NASCENDO numa thread propria (ver `Encomendar`). Nao ha o que fazer
			// alem de deixar o corpo em orbita: o tique volta aqui no proximo quadro, e o pouso
			// acontece sozinho assim que a colisao existir.
			//
			// AVISA UMA VEZ SO. Este metodo roda a cada tique enquanto o corpo esta sobre o disco,
			// e um aviso por tique seriam trinta linhas por segundo no chat de quem esta pousando.
			if (_avisadosDeEspera.Add(pl.Id))
				Avisar(pl, $"{destino.Nome} esta se formando sob voce -- segure a orbita.");
			return false;
		}
		_avisadosDeEspera.Remove(pl.Id);

		MoveToZone(pl.Id, zona, viva.Chegada);

		// A GRAVIDADE DEPOIS do MoveToZone, e nao antes: ele termina chamando `AplicarGravidade`,
		// que le o `planetas.json` -- e la nao ha entrada pra um mundo sorteado, entao ele acabou
		// de cravar 1. Sem esta linha, treinar num planeta de 60x renderia igual a treinar na
		// Terra e nada na tela diria isso.
		AplicarGravidadeGerada(pl, viva.Ficha);

		Avisar(pl, $"voce pousa em {destino.Nome} — {viva.Ficha.Descricao()}.");
		if (viva.ClareiraEscavada)
			Avisar(pl, "nao havia campo aberto aqui: o pouso abriu um na rocha.");
		GD.Print($"[server] {pl.Name} pousou em {destino.Nome} ({viva.Ficha.Descricao()})");
		return true;
	}

	/// <summary>
	/// Poe o peso do chao gerado na ficha. E o mesmo corpo do `AplicarGravidade`, so que lendo a
	/// ficha do mundo em vez do catalogo -- e a duplicacao morre no dia em que o
	/// `AplicarGravidade` consultar <see cref="GravidadeDaZonaGerada"/> (ver o relatorio).
	/// </summary>
	private void AplicarGravidadeGerada(ServerPlayer pl, MundoProcedural ficha)
	{
		if (Math.Abs(pl.Ficha.Planetgrav - ficha.Gravidade) < 1e-9) return;

		pl.Ficha.Planetgrav = ficha.Gravidade;
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		if (ficha.Gravidade > 1)
			Avisar(pl, $"o chão de {ficha.Nome} puxa {ficha.Gravidade:0.##} vezes mais forte. "
					   + "Cada passo custa, e cada treino rende.");
	}

	/// <summary>
	/// DECOLAR DE UM MUNDO GERADO.
	///
	/// O caminho normal (`Decolar`) so acha o planeta de origem entre os PRE-FEITOS, porque so
	/// eles estao em `Espaco.PreFeitos()`. Um mundo sorteado nao esta em lista nenhuma -- ele so
	/// existe enquanto alguem esta nele --, entao quem sabe onde ele fica e o registro daqui.
	///
	/// Devolve falso quando a zona nao e gerada: quem chamou segue com o caminho pre-feito.
	/// </summary>
	public bool DecolarDeProcedural(ServerPlayer pl)
	{
		if (!EhZonaProcedural(pl.Zone)) return false;
		if (!_zonasGeradas.TryGetValue(pl.Zone.Hash, out ZonaGerada? viva)) return false;

		pl.PlanetaDeOrigem = viva.NoEspaco.Nome;
		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(viva.NoEspaco));
		pl.ChunkAtual = ChunkId.De(pl.Pos);   // ver o mesmo carimbo no `Decolar`
		// FORCAR a vizinhanca: ela so sai quando a CHUNK muda, e decolar cai na mesma chunk do
		// planeta. Sem isto o jogador chega ao espaco sem nenhum planeta desenhado -- inclusive
		// sem o que ele acabou de deixar.
		MandarVizinhanca(pl);
		Avisar(pl, $"voce deixa {viva.NoEspaco.Nome} para tras. O silencio do espaco.");
		GD.Print($"[server] {pl.Name} decolou de {viva.NoEspaco.Nome} -> chunk {ChunkId.De(pl.Pos)}");
		return true;
	}
}
