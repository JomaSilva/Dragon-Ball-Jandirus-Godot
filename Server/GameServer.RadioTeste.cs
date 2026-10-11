using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DO RADIO DO SCOUTER -- `--radioteste` ============================
/// O radio e, como a voz, uma afirmacao sobre o que NAO sai do servidor: quem esta sem scouter, noutra
/// frequencia, noutro planeta ou a 501 tiles no espaco nao recebe byte nenhum. Nao ha foto disso. Entao a
/// bancada pergunta ao funil de verdade -- <see cref="QuemOuveORadio"/> e <see cref="RecebiVozDeRadio"/> --
/// com corpos forjados em zonas de verdade, e cada regra tem o DEFEITO posto na frente dela
/// (`RadioDoScouter.*DeTeste`) pra a checagem provar que sabe ficar vermelha.
///
///  1. O APARELHO -- quem fala e quem ouve precisam do scouter LIGADO (na mochila e desligado nao vale).
///  2. A FREQUENCIA -- separa, se troca pelo verbo de producao, e mora na ficha (vai pro save).
///  3. O PLANETA -- o planeta inteiro ouve, a qualquer distancia; outro planeta nao.
///  4. O ESPACO -- 499 tiles ouve, 501 nao; e planeta e espaco nao se misturam.
///  5. OS LUGARES FECHADOS -- nave, mente e Lookout: so quem divide a mesma zona.
///  6. O FUNIL -- torneira propria, mute, a sequencia do falante, e o radio nao vaza pra voz de perto.
///  7. O TETO DE VOZES -- no maximo quatro por ouvinte, e fica quem abriu primeiro.
///  8. O FIO -- os tres pacotes em ida e volta, sem sobrar nem faltar byte.
///
/// O QUE ELA NAO PROVA: que o pacote chegou num cliente (corpo forjado nao tem `Peer`) e nada sobre o som.
/// O fio de verdade e o ouvido esquerdo sao da `--radiovivo` e da `--diagradio` (`Client/RoboDoRadioVivo.cs`,
/// `Client/RoboDeRadio.cs`).
///
///     Godot --headless --path . --server --port 8100 --radioteste
/// =========================================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoRadio = 96_640;

	private int _radioOk, _radioFalhou;

	private void AfirmarRadio(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _radioOk++; GD.Print($"[radio]   OK    {oque}"); return; }
		_radioFalhou++;
		GD.PrintErr($"[radio]   FALHA {oque}   {detalhe}");
	}

	/// <summary>Roda <paramref name="medida"/> com um defeito ligado e devolve o que ela mediu. O botao volta a falso no `finally`.</summary>
	private static T ComDefeito<T>(Action<bool> botao, Func<T> medida)
	{
		botao(true);
		try { return medida(); }
		finally { botao(false); }
	}

	public void RodarBancadaDoRadio()
	{
		_radioOk = _radioFalhou = 0;
		GD.Print("[radio] ============ RADIO DO SCOUTER: o corte e do servidor ============");

		var forjados = new List<ServerPlayer>();
		var contas = new List<string>();

		ServerPlayer Forjar(int i, string nome, ZoneKey zona, Vec2 pos, int frequencia, bool scouter)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoRadio + i,
				Peer = null,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = pos,
				Conta = $"bancada_radio_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 1000 },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			novo.Ficha.FrequenciaDoRadio = frequencia;
			PorNoMundo(novo);
			LigarOScouter(novo, scouter);
			forjados.Add(novo);
			contas.Add(novo.Conta);
			return novo;
		}

		try
		{
			OAparelho(Forjar);
			AFrequencia(Forjar);
			OPlanetaInteiro(Forjar);
			OEspacoTemAlcance(Forjar);
			OsLugaresFechados(Forjar);
			OFunilDoRadio(Forjar);
			OTetoDeVozesDoRadio(Forjar);
			OFioDoRadio();
		}
		catch (Exception e)
		{
			AfirmarRadio($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			RadioDoScouter.SemScouterDeTeste = RadioDoScouter.QualquerFrequenciaDeTeste = false;
			RadioDoScouter.AtravessaMundosDeTeste = RadioDoScouter.EspacoSemLimiteDeTeste = false;
			EspiaoDoRadio = null;
			foreach (ServerPlayer p in forjados)
			{
				EsquecerVoz(p.Id);   // leva o radio junto: e o caminho da saida de um jogador
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}
			foreach (string c in contas) _calados.Remove(c);
			GD.Print($"[radio] ============ {_radioOk} OK, {_radioFalhou} FALHA(S) ============");
		}
	}

	// =====================================================================
	// A PORTA DA `--radiovivo` -- so bancada, e so alcancavel no mesmo processo (`--host`)
	// =====================================================================
	/// <summary>
	/// FORJA UM SEGUNDO CORPO COM SCOUTER, a um tile de <paramref name="aoLadoDe"/> e na frequencia dada, e o
	/// apresenta a zona (`TrocarAparencias`, a porta de todo corpo novo: e por ela que o cliente fica sabendo
	/// o NOME dele). Devolve o id, ou 0. E a outra ponta do radio da `--radiovivo` (`Client/RoboDoRadioVivo.cs`):
	/// ele nao tem `Peer`, entao "fala" pelo <see cref="TransmitirDeTeste"/> e "ouve" pelo <see cref="EspiaoDoRadio"/>.
	/// </summary>
	internal int ForjarCorpoDeRadioDeTeste(int aoLadoDe, string nome, int frequencia)
	{
		if (!_players.TryGetValue(aoLadoDe, out ServerPlayer? eu)) return 0;
		var novo = new ServerPlayer
		{
			Id = IdBaseDoRadio + 900,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = eu.Zone,
			Pos = eu.Pos + new Vec2(ZoneCollision.TileSize, 0),
			Conta = "bancada_radio_forjado",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		novo.Ficha.FrequenciaDoRadio = frequencia;
		PorNoMundo(novo);
		LigarOScouter(novo, true);
		TrocarAparencias(novo);
		return novo.Id;
	}

	/// <summary>Um quadro de audio saindo da boca do corpo forjado, pelo FUNIL de producao: o do radio, ou o da voz de perto.</summary>
	internal void TransmitirDeTeste(int id, ushort seq, byte[] dados, int tam, bool peloRadio)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? p)) return;
		if (peloRadio) RecebiVozDeRadio(p, seq, dados, tam, EstaCalado(p), RelogioDaVoz());
		else RecebiVoz(p, seq, dados, tam, EstaCalado(p));
	}

	/// <summary>Gira o botao do scouter do corpo forjado (direto na ficha: ele nao tem mao pra mandar o verbo).</summary>
	internal void SintonizarCorpoDeTeste(int id, int frequencia)
	{
		if (_players.TryGetValue(id, out ServerPlayer? p)) p.Ficha.FrequenciaDoRadio = frequencia;
	}

	/// <summary>A frequencia que a ficha deste corpo guarda NO SERVIDOR (0 = corpo desconhecido).</summary>
	internal int FrequenciaDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? p) ? FrequenciaDe(p) : 0;

	internal void RemoverCorpoDeRadioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? p) || p.Peer != null) return;
		EsquecerVoz(id);
		RemoverNpc(p);
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	/// <summary>Liga ou desliga o scouter de um corpo forjado pelo MESMO campo que o `Equipar` de producao escreve.</summary>
	private void LigarOScouter(ServerPlayer p, bool ligado)
	{
		if (ligado) p.PoderesConcedidos |= Protocol.Poder.Scouter;
		else p.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		AplicarPoderes(p);
	}

	/// <summary>Quem ouviria esta transmissao AGORA, pelo corte de verdade.</summary>
	private List<ServerPlayer> LequeDoRadio(ServerPlayer a)
	{
		var saida = new List<ServerPlayer>();
		QuemOuveORadio(a, RelogioDaVoz(), saida);
		return saida;
	}

	private bool OuveNoRadio(ServerPlayer falante, ServerPlayer ouvinte) =>
		LequeDoRadio(falante).Any(o => o.Id == ouvinte.Id);

	/// <summary>Duas zonas de planeta pre-feito sem jogador de verdade dentro (NPC nao tem scouter, entao nao atrapalha).</summary>
	private (ZoneKey A, ZoneKey B) DoisPlanetasDeBancada()
	{
		List<string> livres = [.. Espaco.PreFeitos().Select(p => p.Nome)
			.Where(n => !Jogadores.Any(j => string.Equals(j.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))];
		return (ZoneKey.Premade(livres.ElementAtOrDefault(0) ?? "Namek"), ZoneKey.Premade(livres.ElementAtOrDefault(1) ?? "Vegeta"));
	}

	private static Vec2 EmTiles(float x, float y) => new(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);

	// =====================================================================
	// 1. O APARELHO
	// =====================================================================
	private void OAparelho(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print("[radio] -- 1) O APARELHO: quem fala e quem ouve precisam do scouter LIGADO");
		ZoneKey planeta = DoisPlanetasDeBancada().A;

		ServerPlayer fala = forjar(1, "bancada: transmite", planeta, EmTiles(100, 100), 7, true);
		ServerPlayer com = forjar(2, "bancada: com scouter", planeta, EmTiles(300, 300), 7, true);
		ServerPlayer sem = forjar(3, "bancada: sem scouter", planeta, EmTiles(101, 100), 7, false);

		AfirmarRadio("os dois com scouter ligado, na mesma frequencia e no mesmo planeta: OUVE", OuveNoRadio(fala, com));
		AfirmarRadio("**quem esta SEM scouter nao ouve o radio** -- nem colado em quem transmite", !OuveNoRadio(fala, sem));
		AfirmarRadio("(defeito injetado: o scouter nao conta) o sem-scouter passa a ouvir -- a regua sabe reprovar",
			ComDefeito(v => RadioDoScouter.SemScouterDeTeste = v, () => OuveNoRadio(fala, sem)));

		AfirmarRadio("**quem esta SEM scouter nao transmite**: ninguem ouve", LequeDoRadio(sem).Count == 0,
			$"{LequeDoRadio(sem).Count} ouvinte(s)");
		RecebiVozDeRadio(sem, 0, QuadroFalso, QuadroFalso.Length, calado: false, RelogioDaVoz());
		AfirmarRadio("...e pelo funil o quadro dele morre ANTES do leque (a transmissao nem nasce)",
			UltimaSequenciaDoRadioDeTeste(sem.Id) == -1);
		AfirmarRadio("(defeito injetado: o scouter nao conta) o mesmo quadro abre a transmissao",
			ComDefeito(v => RadioDoScouter.SemScouterDeTeste = v, () =>
			{
				RecebiVozDeRadio(sem, 1, QuadroFalso, QuadroFalso.Length, calado: false, RelogioDaVoz());
				return UltimaSequenciaDoRadioDeTeste(sem.Id) >= 0;
			}));
		EsquecerRadio(sem.Id);

		// O APARELHO NA MOCHILA NAO BASTA: tem que estar LIGADO. Pelo verbo de producao (`item_equipar`), que
		// e o que a mochila e a aba Equip mandam.
		Guardar(sem, CatalogoDeItens.Scouter);
		AfirmarRadio("scouter NA MOCHILA mas desligado: continua sem ouvir",
			sem.Mochila.Quantos(CatalogoDeItens.Scouter) > 0 && !OuveNoRadio(fala, sem));
		ComandoDeItem(sem, "item_equipar", CatalogoDeItens.Scouter);
		AfirmarRadio("ligado pelo verbo de producao (`item_equipar`): passa a ouvir", TemScouter(sem) && OuveNoRadio(fala, sem));
		AfirmarRadio("...e a transmitir: quem tem scouter o ouve", OuveNoRadio(sem, com));
		ComandoDeItem(sem, "item_equipar", CatalogoDeItens.Scouter);
		AfirmarRadio("desligado de novo: deixa de ouvir", !TemScouter(sem) && !OuveNoRadio(fala, sem));
	}

	// =====================================================================
	// 2. A FREQUENCIA
	// =====================================================================
	private void AFrequencia(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print("[radio] -- 2) A FREQUENCIA: separa, se troca pelo verbo, e mora na ficha");
		ZoneKey planeta = DoisPlanetasDeBancada().A;

		ServerPlayer fala = forjar(10, "bancada: na 42", planeta, EmTiles(100, 100), 42, true);
		ServerPlayer mesma = forjar(11, "bancada: tambem na 42", planeta, EmTiles(110, 100), 42, true);
		ServerPlayer outra = forjar(12, "bancada: na 43", planeta, EmTiles(101, 100), 43, true);

		AfirmarRadio("mesma frequencia: ouve", OuveNoRadio(fala, mesma));
		AfirmarRadio("**frequencia diferente NAO RECEBE BYTE NENHUM** -- nem colado, nem com scouter", !OuveNoRadio(fala, outra));
		AfirmarRadio("(defeito injetado: a frequencia nao separa) a 43 passa a ouvir a 42",
			ComDefeito(v => RadioDoScouter.QualquerFrequenciaDeTeste = v, () => OuveNoRadio(fala, outra)));

		// A TROCA, pelo despacho de verbos de producao.
		Verbo(outra, RadioDoScouter.VerboDeSintonizar, "42");
		AfirmarRadio("o verbo `radio_frequencia 42` sintoniza: a ficha guarda e ele passa a ouvir",
			outra.Ficha.FrequenciaDoRadio == 42 && OuveNoRadio(fala, outra), $"ficha={outra.Ficha.FrequenciaDoRadio}");
		Verbo(mesma, RadioDoScouter.VerboDeSintonizar, "900");
		AfirmarRadio("...e quem sai da frequencia deixa de ouvir", !OuveNoRadio(fala, mesma));

		foreach (string ruim in new[] { "0", "1000", "-5", "abc", "", "4 2", "42.5" })
			Verbo(outra, RadioDoScouter.VerboDeSintonizar, ruim);
		AfirmarRadio($"numero fora da faixa ({RadioDoScouter.FrequenciaMinima}-{RadioDoScouter.FrequenciaMaxima}) ou que nao e numero e RECUSADO: a frequencia nao muda",
			outra.Ficha.FrequenciaDoRadio == 42, $"ficha={outra.Ficha.FrequenciaDoRadio}");

		// SEM O APARELHO NAO HA O QUE SINTONIZAR (nem ligado, nem na mochila).
		ServerPlayer semNada = forjar(13, "bancada: sem aparelho", planeta, EmTiles(120, 100), 5, false);
		List<string>? antes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		try
		{
			Verbo(semNada, RadioDoScouter.VerboDeSintonizar, "42");
			AfirmarRadio("sem scouter (nem ligado, nem na mochila) o verbo e recusado, com aviso",
				semNada.Ficha.FrequenciaDoRadio == 5 && EscutaDeAvisos.Any(a => a.Contains("scouter", StringComparison.OrdinalIgnoreCase)),
				string.Join(" | ", EscutaDeAvisos));
		}
		finally { EscutaDeAvisos = antes; }

		// SOBREVIVE AO RELOG: o campo e da ficha, e a ficha vai pro save com `IncludeFields` (`CharacterStore`).
		try
		{
			var opcoes = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
			string json = System.Text.Json.JsonSerializer.Serialize(new Fighter { Race = "Human", FrequenciaDoRadio = 42 }, opcoes);
			Fighter? relida = System.Text.Json.JsonSerializer.Deserialize<Fighter>(json, opcoes);
			AfirmarRadio("a frequencia vai pro save com a ficha e volta (sobrevive ao relog)",
				json.Contains("\"FrequenciaDoRadio\":42", StringComparison.Ordinal) && relida?.FrequenciaDoRadio == 42,
				$"relida={relida?.FrequenciaDoRadio}");
			Fighter? antiga = System.Text.Json.JsonSerializer.Deserialize<Fighter>("{}", opcoes);
			AfirmarRadio($"save antigo (sem o campo) nasce na frequencia de fabrica ({RadioDoScouter.FrequenciaPadrao})",
				antiga?.FrequenciaDoRadio == RadioDoScouter.FrequenciaPadrao, $"{antiga?.FrequenciaDoRadio}");
		}
		catch (Exception e)
		{
			AfirmarRadio("a ficha com a frequencia vai e volta pelo serializador do save", false, e.Message);
		}
		AfirmarRadio("frequencia invalida gravada a mao no save cai na de fabrica (ninguem fica num canal que nao existe)",
			RadioDoScouter.Sanear(0) == RadioDoScouter.FrequenciaPadrao && RadioDoScouter.Sanear(5000) == RadioDoScouter.FrequenciaPadrao
			&& RadioDoScouter.Sanear(42) == 42);
	}

	// =====================================================================
	// 3. O PLANETA
	// =====================================================================
	private void OPlanetaInteiro(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print("[radio] -- 3) O PLANETA: o planeta inteiro ouve; outro planeta nao");
		(ZoneKey aqui, ZoneKey la) = DoisPlanetasDeBancada();

		ServerPlayer fala = forjar(20, "bancada: num canto do planeta", aqui, EmTiles(5, 5), 9, true);
		ServerPlayer longe = forjar(21, "bancada: no canto oposto", aqui, EmTiles(480, 480), 9, true);
		ServerPlayer fora = forjar(22, "bancada: noutro planeta", la, EmTiles(5, 5), 9, true);

		float tiles = Vec2.Distance(fala.Pos, longe.Pos) / ZoneCollision.TileSize;
		AfirmarRadio($"a zona da bancada E um planeta ({aqui.Name})", Espaco.EhPlaneta(aqui) && Espaco.EhPlaneta(la) && aqui.Hash != la.Hash);
		AfirmarRadio($"**NO MESMO PLANETA O RADIO CHEGA A QUALQUER DISTANCIA** ({tiles:0} tiles, mais que os {RadioDoScouter.AlcanceNoEspacoEmTiles} do espaco)",
			tiles > RadioDoScouter.AlcanceNoEspacoEmTiles && OuveNoRadio(fala, longe));

		// O CONTRASTE: a voz de PERTO, entre os mesmos dois corpos, nao chega -- o radio nao e a voz com outro nome.
		var dePerto = new List<(ServerPlayer Quem, byte Dist, bool Parede)>();
		QuemOuveAVoz(fala, RelogioDaVoz(), dePerto);
		AfirmarRadio("...e a voz de PERTO entre os mesmos dois nao chega (sao dois cortes diferentes)",
			!dePerto.Any(l => l.Quem.Id == longe.Id));

		AfirmarRadio("**OUTRO PLANETA NAO RECEBE BYTE NENHUM** -- mesma frequencia, scouter ligado, mesma coordenada", !OuveNoRadio(fala, fora));
		AfirmarRadio("(defeito injetado: o radio atravessa mundos) o outro planeta passa a ouvir",
			ComDefeito(v => RadioDoScouter.AtravessaMundosDeTeste = v, () => OuveNoRadio(fala, fora)));

		// O MUNDO SORTEADO E PLANETA TAMBEM: a regra pura, com a chave de um mundo gerado.
		ZoneKey gerado = ZoneKey.Procedural("Verdejante-1042", 1042), vizinho = ZoneKey.Procedural("Verdejante-1042", 1043);
		AfirmarRadio("planeta SORTEADO: o planeta inteiro ouve, e o de seed vizinha (mesmo nome) nao",
			Espaco.EhPlaneta(gerado)
			&& RadioDoScouter.Ouve(true, 9, gerado, EmTiles(0, 0), true, 9, gerado, EmTiles(900, 900))
			&& !RadioDoScouter.Ouve(true, 9, gerado, EmTiles(0, 0), true, 9, vizinho, EmTiles(0, 0)));
	}

	// =====================================================================
	// 4. O ESPACO
	// =====================================================================
	private void OEspacoTemAlcance(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print($"[radio] -- 4) O ESPACO: ate {RadioDoScouter.AlcanceNoEspacoEmTiles} tiles, e nem um a mais");
		ZoneKey espaco = Espaco.Zona(SeedDoUniverso);

		// LONGE DA ORIGEM de proposito (a Terra e o zero do universo): com coordenadas de um milhao de pixels, a
		// conta em `float` ja arredondaria a diferenca -- e e la que os jogadores viajam.
		var centro = new Vec2(1_400_000f, -900_000f);
		const float t = ZoneCollision.TileSize;

		ServerPlayer fala = forjar(30, "bancada: transmite do espaco", espaco, centro, 3, true);
		ServerPlayer a499 = forjar(31, "bancada: a 499 tiles", espaco, centro + new Vec2(499 * t, 0), 3, true);
		ServerPlayer a500 = forjar(32, "bancada: a 500 tiles", espaco, centro + new Vec2(0, -500 * t), 3, true);
		ServerPlayer a501 = forjar(33, "bancada: a 501 tiles", espaco, centro + new Vec2(-501 * t, 0), 3, true);
		ServerPlayer naDiagonal = forjar(34, "bancada: 400 + 400 tiles na diagonal", espaco, centro + new Vec2(400 * t, 400 * t), 3, true);

		AfirmarRadio("a zona da bancada E o espaco (uma zona so pro universo), e nao um planeta",
			Espaco.EhEspaco(espaco) && !Espaco.EhPlaneta(espaco));
		AfirmarRadio("no espaco, a 499 tiles: OUVE", OuveNoRadio(fala, a499));
		AfirmarRadio("a 500 tiles exatos: ouve (o limite e inclusivo)", OuveNoRadio(fala, a500));
		AfirmarRadio("**A 501 TILES NAO RECEBE BYTE NENHUM**", !OuveNoRadio(fala, a501));
		AfirmarRadio("a distancia e em linha reta: 400 + 400 na diagonal sao 566 tiles, e nao chega", !OuveNoRadio(fala, naDiagonal));
		AfirmarRadio("(defeito injetado: o espaco nao tem limite) os de 501 e de 566 tiles passam a ouvir",
			ComDefeito(v => RadioDoScouter.EspacoSemLimiteDeTeste = v, () => OuveNoRadio(fala, a501) && OuveNoRadio(fala, naDiagonal)));
		AfirmarRadio("e simetrico: quem ouve a 499 tambem e ouvido de la", OuveNoRadio(a499, fala) && !OuveNoRadio(a501, fala));

		// PLANETA E ESPACO NAO SE MISTURAM: o pedido diz "no mesmo planeta" OU "a 500 tiles no espaco".
		ServerPlayer noChao = forjar(35, "bancada: no chao de um planeta", DoisPlanetasDeBancada().A, centro, 3, true);
		AfirmarRadio("do espaco pro chao de um planeta nao chega (nem na mesma coordenada), e nem o contrario",
			!OuveNoRadio(fala, noChao) && !OuveNoRadio(noChao, fala));

		AfirmarRadio($"o alcance em pixels e o dos tiles ({RadioDoScouter.AlcanceNoEspacoEmTiles} x {ZoneCollision.TileSize})",
			Mathf.IsEqualApprox(RadioDoScouter.AlcanceNoEspacoPx, RadioDoScouter.AlcanceNoEspacoEmTiles * ZoneCollision.TileSize));
	}

	// =====================================================================
	// 5. OS LUGARES FECHADOS
	// =====================================================================
	private void OsLugaresFechados(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print("[radio] -- 5) NAVE, MENTE E LOOKOUT: so quem divide a mesma zona");
		Vec2 p = EmTiles(40, 40);
		ZoneKey nave = Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(977), outraNave = Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(978);

		ServerPlayer naPonte = forjar(40, "bancada: na ponte da nave", nave, p, 8, true);
		ServerPlayer noPorao = forjar(41, "bancada: no porao da mesma nave", nave, p + EmTiles(30, 0), 8, true);
		ServerPlayer naOutra = forjar(42, "bancada: noutra nave", outraNave, p, 8, true);
		ServerPlayer naMente = forjar(43, "bancada: dentro de uma mente", DimensaoMental.De(977), p, 8, true);
		ServerPlayer noLookout = forjar(44, "bancada: no Lookout", ZoneKey.Premade("Lookout"), p, 8, true);
		ServerPlayer noLookout2 = forjar(45, "bancada: tambem no Lookout", ZoneKey.Premade("Lookout"), p + EmTiles(60, 0), 8, true);

		AfirmarRadio("dentro da MESMA nave o radio chega", OuveNoRadio(naPonte, noPorao));
		AfirmarRadio("de uma nave pra outra, pra uma mente ou pro Lookout: nao chega",
			!OuveNoRadio(naPonte, naOutra) && !OuveNoRadio(naPonte, naMente) && !OuveNoRadio(naPonte, noLookout));
		AfirmarRadio("o Lookout nao e planeta, e quem esta nele se ouve entre si (mesma zona)",
			!Espaco.EhPlaneta(noLookout.Zone) && OuveNoRadio(noLookout, noLookout2));
		AfirmarRadio("...mas o Lookout nao ouve a Terra, nem a Terra o Lookout (sao zonas diferentes)",
			!RadioDoScouter.Alcanca(ZoneKey.Premade("Lookout"), p, ZoneKey.Premade("Earth"), p)
			&& !RadioDoScouter.Alcanca(ZoneKey.Premade("Earth"), p, ZoneKey.Premade("Lookout"), p));
	}

	// =====================================================================
	// 6. O FUNIL
	// =====================================================================
	private void OFunilDoRadio(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print("[radio] -- 6) O FUNIL: torneira propria, mute, sequencia, e o radio nao vaza pra voz de perto");
		ZoneKey planeta = DoisPlanetasDeBancada().A;

		ServerPlayer fala = forjar(50, "bancada: o radialista", planeta, EmTiles(200, 200), 11, true);
		ServerPlayer noRadio = forjar(51, "bancada: longe, na frequencia", planeta, EmTiles(400, 400), 11, true);
		ServerPlayer aoLado = forjar(52, "bancada: ao lado, SEM scouter", planeta, EmTiles(201, 200), 11, false);
		ServerPlayer aoLadoNoutra = forjar(53, "bancada: ao lado, noutra frequencia", planeta, EmTiles(200, 201), 12, true);

		var entregas = new List<(int De, int Pra, int Bytes)>();
		EspiaoDoRadio = (de, pra, bytes) => entregas.Add((de, pra, bytes));
		long agora = RelogioDaVoz();

		// ---- o radio vai pra quem esta NA FREQUENCIA, e so pra ele ----
		RecebiVozDeRadio(fala, 0, QuadroFalso, QuadroFalso.Length, calado: false, agora);
		AfirmarRadio("um quadro de radio pelo funil vai pro ouvinte da frequencia, com os bytes do audio",
			entregas.Count(e => e.De == fala.Id && e.Pra == noRadio.Id && e.Bytes == QuadroFalso.Length) == 1,
			string.Join(", ", entregas.Select(e => $"{e.De}->{e.Pra}:{e.Bytes}")));
		AfirmarRadio("**E NAO VAZA PRA QUEM ESTA AO LADO**: o sem-scouter e o de outra frequencia nao recebem nada",
			!entregas.Any(e => e.Pra == aoLado.Id || e.Pra == aoLadoNoutra.Id));
		AfirmarRadio("...e a voz de PERTO do radialista nem abriu (segurar so a tecla do radio nao fala pra sala)",
			!FalandoPorVozDeTeste(fala.Id) && !TemSequenciaDeVozDeTeste(fala.Id));

		// ---- e a voz de perto nao vaza pro radio ----
		entregas.Clear();
		RecebiVoz(fala, 0, QuadroFalso, QuadroFalso.Length, calado: false);
		var dePerto = new List<(ServerPlayer Quem, byte Dist, bool Parede)>();
		QuemOuveAVoz(fala, RelogioDaVoz(), dePerto);
		AfirmarRadio("um quadro de voz de PERTO vai pra quem esta ao lado (com scouter ou sem) e nao pro radio de quem esta longe",
			entregas.Count == 0 && dePerto.Any(l => l.Quem.Id == aoLado.Id) && dePerto.Any(l => l.Quem.Id == aoLadoNoutra.Id)
			&& !dePerto.Any(l => l.Quem.Id == noRadio.Id),
			$"radio={entregas.Count} perto=[{string.Join(",", dePerto.Select(l => l.Quem.Id - IdBaseDoRadio))}]");

		// ---- as duas torneiras sao separadas: as duas teclas seguradas nao picotam ----
		ServerPlayer duplo = forjar(54, "bancada: as duas teclas", planeta, EmTiles(200, 205), 11, true);
		for (int i = 0; i < 250; i++)   // 5 s, um quadro de cada a cada 20 ms
		{
			long quando = agora + 1000 + i * VozLocal.MsPorQuadro;
			RecebiVozDeRadio(duplo, (ushort)i, QuadroFalso, QuadroFalso.Length, calado: false, quando);
		}
		for (int i = 0; i < 10; i++) RecebiVoz(duplo, (ushort)i, QuadroFalso, QuadroFalso.Length, calado: false);
		AfirmarRadio("5 s de radio no ritmo certo passam INTEIROS, e a voz de perto ao mesmo tempo tambem (torneiras separadas)",
			QuadrosDeRadioRecusadosDeTeste(duplo.Id) == 0 && QuadrosDeVozRecusadosDeTeste(duplo.Id) == 0,
			$"radio recusou {QuadrosDeRadioRecusadosDeTeste(duplo.Id)}, perto recusou {QuadrosDeVozRecusadosDeTeste(duplo.Id)}");

		// ---- a torneira do radio segura um cliente modificado ----
		ServerPlayer torneira = forjar(55, "bancada: a torneira", planeta, EmTiles(210, 200), 11, true);
		for (int i = 0; i < 200; i++) RecebiVozDeRadio(torneira, (ushort)i, QuadroFalso, QuadroFalso.Length, calado: false, agora);
		AfirmarRadio($"200 quadros de radio no mesmo instante: so a folga ({VozLocal.RajadaDeQuadros}) passa",
			QuadrosDeRadioRecusadosDeTeste(torneira.Id) == 200 - (int)VozLocal.RajadaDeQuadros,
			$"recusados {QuadrosDeRadioRecusadosDeTeste(torneira.Id)}");
		RecebiVozDeRadio(torneira, 0, new byte[VozLocal.MaxBytesDeQuadro + 1], VozLocal.MaxBytesDeQuadro + 1, calado: false, agora + 5000);
		AfirmarRadio("quadro maior que o teto e recusado",
			QuadrosDeRadioRecusadosDeTeste(torneira.Id) == 200 - (int)VozLocal.RajadaDeQuadros + 1);

		// ---- o mute de admin cala o radio pela mesma marca do texto e da voz ----
		ServerPlayer mudo = forjar(56, "bancada: o calado", planeta, EmTiles(220, 200), 11, true);
		_calados.Add(mudo.Conta);
		entregas.Clear();
		RecebiVozDeRadio(mudo, 0, QuadroFalso, QuadroFalso.Length, EstaCalado(mudo), agora);
		AfirmarRadio("**quem esta CALADO por admin nao transmite**: o quadro morre antes do leque",
			EstaCalado(mudo) && UltimaSequenciaDoRadioDeTeste(mudo.Id) == -1 && entregas.Count == 0);
		_calados.Remove(mudo.Conta);
		RecebiVozDeRadio(mudo, 0, QuadroFalso, QuadroFalso.Length, EstaCalado(mudo), agora + 20);
		AfirmarRadio("solto o mute, o mesmo quadro atravessa", UltimaSequenciaDoRadioDeTeste(mudo.Id) == 0 && entregas.Any(e => e.De == mudo.Id));

		// ---- nocauteado e morto transmitem, como falam de perto ----
		ServerPlayer caido = forjar(57, "bancada: nocauteado", planeta, EmTiles(230, 200), 11, true);
		caido.Ficha.KO = true;
		ServerPlayer morto = forjar(58, "bancada: morto", planeta, EmTiles(240, 200), 11, true);
		morto.Ficha.dead = true;
		AfirmarRadio("nocauteado e morto continuam transmitindo e ouvindo (paridade com a voz e o texto, que nunca calaram quem esta no chao)",
			OuveNoRadio(caido, noRadio) && OuveNoRadio(morto, noRadio) && OuveNoRadio(fala, caido) && OuveNoRadio(fala, morto));

		// ---- a sequencia do FALANTE atravessa o relay do radio (a perda da subida deixa buraco) ----
		ServerPlayer comBuraco = forjar(59, "bancada: a subida com perda", planeta, EmTiles(250, 200), 11, true);
		var carimbos = new List<int>();
		long quandoBuraco = agora + 20_000;
		foreach (ushort doFalante in new ushort[] { 500, 501, 503 })
		{
			RecebiVozDeRadio(comBuraco, doFalante, QuadroFalso, QuadroFalso.Length, calado: false, quandoBuraco += 20);
			carimbos.Add(UltimaSequenciaDoRadioDeTeste(comBuraco.Id));
		}
		AfirmarRadio("o falante manda 500, 501, 503 pelo radio e o relay carimba 0, 1, 3 (o buraco fica pra quem ouve remendar)",
			carimbos.SequenceEqual([0, 1, 3]), string.Join(",", carimbos));

		// ---- sair do servidor leva a torneira e a transmissao (id se reusa) ----
		EsquecerVoz(comBuraco.Id);
		AfirmarRadio("o `EsquecerVoz` da saida leva o radio junto (torneira e transmissao)",
			UltimaSequenciaDoRadioDeTeste(comBuraco.Id) == -1 && QuadrosDeRadioRecusadosDeTeste(torneira.Id) > 0
			&& !_torneirasDoRadio.ContainsKey(comBuraco.Id));
		EspiaoDoRadio = null;
	}

	// =====================================================================
	// 7. O TETO DE VOZES
	// =====================================================================
	private void OTetoDeVozesDoRadio(Func<int, string, ZoneKey, Vec2, int, bool, ServerPlayer> forjar)
	{
		GD.Print($"[radio] -- 7) O TETO: no maximo {RadioDoScouter.MaxVozesPorOuvinte} vozes de radio por ouvinte, e fica quem abriu primeiro");
		ZoneKey planeta = DoisPlanetasDeBancada().B;
		long t0 = RelogioDaVoz() + 60_000;

		ServerPlayer ouvinte = forjar(70, "bancada: o ouvinte", planeta, EmTiles(50, 50), 77, true);

		// SEIS TRANSMISSOES NA MESMA FREQUENCIA, abertas com 10 ms de diferenca -- e forjadas na ordem INVERSA
		// dos ids, pra "o menor id" e "a ordem da lista" nao passarem por "quem abriu primeiro".
		var falantes = new List<ServerPlayer>();
		for (int i = 0; i < 6; i++)
			falantes.Add(forjar(80 - i, $"bancada: transmissao {i}", planeta, EmTiles(60 + i * 40, 60), 77, true));
		for (int i = 0; i < 6; i++)
			RecebiVozDeRadio(falantes[i], 0, QuadroFalso, QuadroFalso.Length, calado: false, t0 + i * 10);

		// AS TRANSMISSOES NO AR em `quando` que chegam ao ouvinte, pelo corte de verdade.
		List<int> Ouvidas(long quando)
		{
			var l = new List<int>();
			for (int i = 0; i < 6; i++)
			{
				if (!_transmissoes.TryGetValue(falantes[i].Id, out TransmissaoDeRadio? t) || !t.NoAr(quando)) continue;
				var leque = new List<ServerPlayer>();
				QuemOuveORadio(falantes[i], quando, leque);
				if (leque.Any(o => o.Id == ouvinte.Id)) l.Add(i);
			}
			return l;
		}

		List<int> agora = Ouvidas(t0 + 60);
		AfirmarRadio($"com seis transmitindo, o ouvinte recebe {RadioDoScouter.MaxVozesPorOuvinte}",
			agora.Count == RadioDoScouter.MaxVozesPorOuvinte, string.Join(",", agora));
		AfirmarRadio("...e sao as QUATRO QUE ABRIRAM PRIMEIRO (quem pegou o canal fica com ele)",
			agora.SequenceEqual([0, 1, 2, 3]), string.Join(",", agora));

		// A VAGA SE SOLTA: as duas primeiras calam (passam os 250 ms sem quadro) e as outras quatro seguem
		// falando sem parar -- as duas que esperavam entram sem ninguem ter de reabrir o canal.
		ushort seq = 1;
		foreach (long quando in new[] { t0 + 200, t0 + 400 })
		{
			for (int i = 2; i < 6; i++)
				RecebiVozDeRadio(falantes[i], seq, QuadroFalso, QuadroFalso.Length, calado: false, quando);
			seq++;
		}
		long depois = t0 + 60 + VozLocal.MsAteCalar + 100;
		List<int> soltou = Ouvidas(depois);
		AfirmarRadio("caladas as duas primeiras, as que esperavam entram (a vaga se solta sozinha)",
			soltou.SequenceEqual([2, 3, 4, 5]), string.Join(",", soltou));

		// QUEM ESTA NOUTRA FREQUENCIA NAO OCUPA VAGA de quem nao o ouve.
		for (int i = 2; i < 5; i++) falantes[i].Ficha.FrequenciaDoRadio = 78;
		List<int> outraFreq = Ouvidas(depois);
		AfirmarRadio("transmissao de OUTRA frequencia nao ocupa vaga: sobra so a que continua na dele",
			outraFreq.SequenceEqual([5]), string.Join(",", outraFreq));
	}

	// =====================================================================
	// 8. O FIO
	// =====================================================================
	/// <summary>
	/// OS TRES PACOTES EM IDA E VOLTA, pelos MESMOS pares escritor/leitor que as duas pontas chamam
	/// (<see cref="FioDoRadio"/>). O que se cobra e o que a memoria do port ensinou: nao sobrar nem faltar
	/// byte -- o leitor do LiteNetLib nao confere o fim do pacote.
	/// </summary>
	private void OFioDoRadio()
	{
		GD.Print("[radio] -- 8) O FIO: os tres pacotes, escritos e lidos pelo mesmo par");
		var audio = new byte[57];
		for (int i = 0; i < audio.Length; i++) audio[i] = (byte)(i * 7 + 3);
		var volta = new byte[VozLocal.MaxBytesDeQuadro];

		// ---- C2S.VozDeRadio ----
		var w = Protocol.Begin(Protocol.C2S.VozDeRadio);
		FioDoRadio.EscreverQuadroDeQuemFala(w, 40_123, audio, audio.Length);
		var r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		byte op = r.GetByte();
		bool leu = FioDoRadio.LerQuadroDeQuemFala(r, volta, out ushort seq, out int tam);
		AfirmarRadio("`C2S.VozDeRadio` vai e volta: opcode, sequencia, tamanho e os bytes do audio, sem sobrar nem faltar byte",
			op == (byte)Protocol.C2S.VozDeRadio && leu && seq == 40_123 && tam == audio.Length
			&& volta.AsSpan(0, tam).SequenceEqual(audio) && r.AvailableBytes == 0, $"seq={seq} tam={tam} sobra={r.AvailableBytes}");

		// ---- S2C.VozDeRadio ----
		w = Protocol.Begin(Protocol.S2C.VozDeRadio);
		FioDoRadio.EscreverQuadroPraQuemOuve(w, 123_456, 65_535, audio, audio.Length);
		r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		op = r.GetByte();
		Array.Clear(volta);
		leu = FioDoRadio.LerQuadroPraQuemOuve(r, volta, out int falante, out seq, out tam);
		AfirmarRadio("`S2C.VozDeRadio` vai e volta: opcode, falante, sequencia, tamanho e audio, sem sobrar nem faltar byte",
			op == (byte)Protocol.S2C.VozDeRadio && leu && falante == 123_456 && seq == 65_535 && tam == audio.Length
			&& volta.AsSpan(0, tam).SequenceEqual(audio) && r.AvailableBytes == 0, $"falante={falante} seq={seq} tam={tam} sobra={r.AvailableBytes}");

		// O CONTRA-EXEMPLO DA REGUA: o leitor ERRADO no mesmo pacote (o do cliente->servidor, que nao sabe do
		// `int falante`) nao devolve o que foi escrito. Se devolvesse, "foi e voltou" nao mediria nada.
		r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		r.GetByte();
		bool errado = FioDoRadio.LerQuadroDeQuemFala(r, volta, out ushort seqErrada, out int tamErrado);
		AfirmarRadio("(defeito injetado: o leitor de um pacote lendo o outro) a ida e volta REPROVA",
			!(errado && seqErrada == 65_535 && tamErrado == audio.Length && r.AvailableBytes == 0),
			$"leu={errado} seq={seqErrada} tam={tamErrado} sobra={r.AvailableBytes}");

		// ---- tamanho mentiroso: descartado ANTES de ler o audio ----
		var mentira = new LiteNetLib.Utils.NetDataWriter();
		mentira.Put((ushort)1);
		mentira.Put((byte)150);          // promete 150 bytes...
		mentira.Put(audio, 0, 20);       // ...e traz 20
		AfirmarRadio("quadro que promete mais audio do que trouxe e DESCARTADO sem ler alem do fim",
			!FioDoRadio.LerQuadroDeQuemFala(new LiteNetLib.Utils.NetDataReader(mentira.CopyData()), volta, out _, out _));
		var gordo = new LiteNetLib.Utils.NetDataWriter();
		gordo.Put((ushort)1);
		gordo.Put((byte)(VozLocal.MaxBytesDeQuadro + 1));
		gordo.Put(new byte[VozLocal.MaxBytesDeQuadro + 1]);
		AfirmarRadio($"quadro maior que o teto ({VozLocal.MaxBytesDeQuadro} B) e descartado",
			!FioDoRadio.LerQuadroDeQuemFala(new LiteNetLib.Utils.NetDataReader(gordo.CopyData()), volta, out _, out _));

		// ---- S2C.Radio ----
		w = Protocol.Begin(Protocol.S2C.Radio);
		FioDoRadio.EscreverEstado(w, 987);
		r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		op = r.GetByte();
		int freq = FioDoRadio.LerEstado(r);
		AfirmarRadio("`S2C.Radio` vai e volta: a frequencia, em 2 bytes",
			op == (byte)Protocol.S2C.Radio && freq == 987 && r.AvailableBytes == 0, $"freq={freq} sobra={r.AvailableBytes}");

		// OS TRES OPCODES SAO OS DO BLOCO RESERVADO, e nenhum pisa num vizinho.
		AfirmarRadio("os opcodes do radio sao C2S 26, S2C 64 e S2C 65, sem repetir numero de outro pacote",
			(byte)Protocol.C2S.VozDeRadio == 26 && (byte)Protocol.S2C.VozDeRadio == 64 && (byte)Protocol.S2C.Radio == 65
			&& Enum.GetValues<Protocol.C2S>().Select(v => (byte)v).Distinct().Count() == Enum.GetValues<Protocol.C2S>().Length
			&& Enum.GetValues<Protocol.S2C>().Select(v => (byte)v).Distinct().Count() == Enum.GetValues<Protocol.S2C>().Length);
	}
}
