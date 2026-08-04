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
	/// Esta zona e um planeta GERADO?
	///
	/// O espaco fica de fora de proposito: ele tambem e `KindProcedural` (ver `Espaco.Zona`), mas
	/// nao tem chao, nao tem colisao e nao se pousa nele -- ele E o lugar de onde se pousa.
	/// </summary>
	public static bool EhZonaProcedural(ZoneKey z) =>
		z.Kind == ZoneKey.KindProcedural && !Espaco.EhEspaco(z);

	/// <summary>
	/// O mundo desta zona, gerando na hora se for a primeira vez.
	///
	/// <paramref name="noEspaco"/> so e usado quando a zona ainda nao existe: e o planeta do mapa
	/// do universo, guardado pra saber pra onde a decolagem devolve o corpo.
	/// </summary>
	public ZonaGerada? MundoDaZona(ZoneKey zona, PlanetaNoEspaco? noEspaco = null)
	{
		if (!EhZonaProcedural(zona)) return null;
		if (_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? viva)) return viva;
		if (noEspaco == null) return null;   // sem o planeta de origem nao da pra montar a ficha

		PodarZonasVazias();

		MundoProcedural ficha = MundoProcedural.DaSeed(zona.Seed, zona.Name);
		ulong t0 = Time.GetTicksUsec();
		TerrenoGerado t = GeradorDeTerreno.Gerar(ficha.Parametros());
		ulong us = Time.GetTicksUsec() - t0;

		viva = new ZonaGerada
		{
			Ficha = ficha,
			Colisao = t.Colisao,
			Chegada = t.Spawn,
			NoEspaco = noEspaco.Value,
			ClareiraEscavada = t.ClareiraEscavada,
		};
		_zonasGeradas[zona.Hash] = viva;

		// A ASSINATURA VAI NO LOG DE PROPOSITO. E o unico jeito barato de descobrir que cliente e
		// servidor divergiram: o numero daqui tem que bater com o que o cliente imprime ao entrar.
		GD.Print($"[server] gerado {ficha.Nome}: {ficha.Descricao()} em {us / 1000.0:0.0} ms "
				 + $"| chegada ({t.SpawnCelX},{t.SpawnCelY}) | assinatura {t.Assinatura():X16}"
				 + (t.ClareiraEscavada ? " | CLAREIRA ESCAVADA" : ""));
		return viva;
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
			Avisar(pl, $"{destino.Nome} nao quis nascer -- fique em orbita por enquanto.");
			GD.PushWarning($"[server] falhei em gerar {destino.Nome} (seed {destino.Seed})");
			return false;
		}

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
