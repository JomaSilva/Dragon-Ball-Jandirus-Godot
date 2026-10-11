using System.Globalization;
using System.Reflection;
using Godot;
using Jandirus.Core;
using Jandirus.Core.Social;
using Jandirus.Core.Torneio;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA PARADA (`--paradateste`) -- o servidor que PARA e SOBE DE NOVO no mesmo processo.
///
/// O dono (2026-10-11): *"sim pode consertar o stop do servidor"*; *"pode consertar os 4, faca a parada derrubar
/// cada jogador"*; e *"Sim clicar no x deveria salvar, o torneio deve ser cancelado nesse caso de parada, a
/// fusao e perdoada"*. Os defeitos, as duas paradas e a tabela do que a parada faz com cada efeito do `Drop`
/// estao no cabecalho do `GameServer.Parada.cs`.
///
/// ============================ O CICLO DE VERDADE, E NAO UM RETRATO DELE ============================
/// E o gesto de quem hospeda, sai e hospeda de novo, feito pelas portas de PRODUCAO e pelo FIO:
///
///     porta aberta -> clientes entram (aperto de mao, login) -> `SalvarEParar()` -> `Start(porta)` -> entram de novo
///
/// Os clientes sao crus e PERSISTENTES (`GameServer.ClientesCrusDeTeste.cs`): o mesmo objeto atravessa as
/// paradas, entao quem volta disca DO MESMO SOQUETE -- como o cliente do jogo, que nunca fecha o dele. A (so a
/// selecao), B e C (no mundo) e D (outro soquete, pedindo a conta de A). Doze subidas, doze paradas:
///
///   PARTE I -- o caminho de quem hospeda, SEM defeito nenhum (tres `SalvarEParar` seguidos):
///     1. dois FUNDIDOS na parada: os dois vao pro disco como pessoas, e NINGUEM PAGA A RECARGA;
///     2. o mundo da subida seguinte nasce POVOADO, com os mesmos habitantes do boot; quem hospeda sozinho e
///        ADMIN pela prova desta hospedagem (e so ele), e a prova morre na parada; a mesma conta loga de novo;
///        e o CONTROLE da fusao: no logout normal a recarga continua sendo cobrada, e o resto do que vai pro
///        disco e igual ao que a parada grava;
///     3. alguem na SALA DO TEMPO e um TORNEIO em disputa na parada: a subida seguinte comeca com a sala vazia
///        (a vaga volta no login) e SEM torneio -- cancelado, ninguem perdeu por W.O., quem estava no palco
///        acorda la, livre;
///     4. dois num ZANZOCLASH na parada: ninguem e recolocado, ninguem apanha, ninguem e gravado oculto;
///     e A REGUA GERAL: o retrato do servidor que subiu do zero contra o de depois das tres paradas.
///     7b. O ESTRAGO DA SESSAO (fica depois da parte 7): parede, arvore, rocha e mobilia derrubadas na Terra, uma porta
///        aberta, uma montanha num mundo sorteado e uma parede no interior de uma nave -- depois da parada o mapa
///        volta inteiro, a lista zera e o cliente que entra nao recebe buraco. Contra-exemplo: a parada que lembra.
///     4b. O SERVIDOR DEDICADO: o criterio "producao x bancada" (a linha de comando), fechar pela porta de saida
///        do processo grava tudo (o X da janela, e o pedido que vem de outra thread, como o do console) -- e
///        os contra-exemplos: `Saida.DedicadoNaoGravaDeTeste`, e a corrida de bancada, que continua sem gravar;
///   PARTE II -- os CONTRA-EXEMPLOS, um botao por conserto:
///     5. `ParadaNaoDerrubaDeTeste`: os fundidos nao vao pro disco;
///     6. `ParadaCobraAFusaoDeTeste`: a parada cobra a recarga da fusao que ela mesma desfez;
///     7. `ParadaLembraDaSessaoDeTeste`, com o `Stop` SOZINHO: a conta que nao loga mais (muda), o mundo
///        vazio, a sala ocupada por quem nao esta, o torneio que sobrevive e decide luta por AUSENCIA, a prova
///        da hospedagem anterior ainda valendo -- e o `Stop` sozinho, com e sem defeito, nao grava ninguem (de proposito);
///     9. `SaidaDoEmbateDeAntesDeTeste`, na ultima parada: o primeiro a sair do embate vai pro disco oculto.
///
/// ELA RODA COM A PORTA ABERTA, como a `--versaoteste`, e bombeia a rede ela mesma (a arvore ainda nao esta
/// processando o servidor nesta hora) -- por isso o povoamento e o torneio andam pelos tiques de producao
/// (`TickDoPovoamento`, `TickDoTorneio`), chamados daqui. A REENTRADA: a subida de dentro da bancada rele a
/// linha de comando e chegaria aqui de novo -- o trinco `_paradaRodando` a manda embora, e a bancada confere.
///
/// `--paradaantiga` (junto da flag da bancada): a PARTE I roda com os defeitos ligados -- e o PLACAR DE ANTES
/// dos consertos, no mesmo binario, e ele TEM de sair vermelho (o caso `parada_antes` do lancador).
///
///     Godot --headless --path . --server --port 8284 --paradateste
/// </summary>
public partial class GameServer
{
	private int _pdOk, _pdFalhou;

	/// <summary>A bancada da parada esta no meio do ciclo dela (as subidas que ELA faz nao a rodam de novo), e ja rodou neste processo.</summary>
	private bool _paradaRodando, _paradaJaRodou;

	/// <summary>Quantas vezes a subida do servidor chamou a bancada da parada (uma por `Start` com a flag).</summary>
	private int _paradaChamadas;

	/// <summary>Quantos tiles separam os dois corpos de bancada no palco da fusao e do embate.</summary>
	private const int TilesEntreOsDoisDaParada = 4;

	/// <summary>O teto de tiques do censo da bancada: o povoamento inteiro cabe em algumas centenas.</summary>
	private const int TetoDeTiquesDoCensoDaParada = 4000;

	/// <summary>Quantas linhas de "sobrou" a regua geral imprime, no maximo.</summary>
	private const int TetoDeSobrasNoLogDaParada = 120;

	/// <summary>Os relogios do torneio de bancada, em segundos: o W.O., a contagem e o intervalo (os de producao sao 30, 5 e 10).</summary>
	private const double WoDoTorneioDaParada = 0.2, ContagemDoTorneioDaParada = 0, IntervaloDoTorneioDaParada = 0.05;

	private void AfirmarPd(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pdOk++; GD.Print($"[parada]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pdFalhou++; GD.PrintErr($"[parada]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>
	/// O RETRATO DO SERVIDOR, pra regua geral: cada campo de instancia do `GameServer` -- colecao (quantos itens
	/// tem), valor simples (quanto vale) ou objeto (se existe). Tirado por reflexao de proposito: uma lista de
	/// campos escrita a mao so mediria o que alguem lembrou de por nela.
	/// </summary>
	private SortedDictionary<string, string> RetratoDoServidorDeTeste()
	{
		var retrato = new SortedDictionary<string, string>(StringComparer.Ordinal);
		const BindingFlags DeInstancia = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		foreach (FieldInfo campo in typeof(GameServer).GetFields(DeInstancia))
		{
			object? v = campo.GetValue(this);
			retrato[campo.Name] = v switch
			{
				null => "nulo",
				string texto => $"\"{texto}\"",
				bool or byte or short or int or uint or long or ulong or float or double or Enum
					=> Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
				System.Collections.ICollection colecao => $"{colecao.Count} item(ns)",
				// o `HashSet` nao e `ICollection` (so a generica): a contagem dele sai pela propriedade
				System.Collections.IEnumerable when v.GetType().GetProperty("Count", typeof(int))?.GetValue(v) is int quantos => $"{quantos} item(ns)",
				_ => "objeto",
			};
		}
		return retrato;
	}

	// =====================================================================
	// O ESTRAGO DA SESSAO (parte 7b do roteiro)
	// =====================================================================

	/// <summary>O PALCO DO ESTRAGO: o que a bancada derrubou, abriu e pos de pe antes da parada -- e onde.</summary>
	private sealed record EstragoDaParada(
		ZoneKey Terra, (int X, int Y) Parede, (int X, int Y) Arvore, (int X, int Y) Rocha, (int X, int Y) Mobilia,
		string ZonaDaPorta, (int X, int Y) Porta,
		PlanetaNoEspaco Planeta, ZoneKey Mundo, (int X, int Y) Montanha,
		ZoneKey Interior, (int X, int Y) ParedeDoInterior);

	/// <summary>O QUE HA DO ESTRAGO AGORA, lido de onde ele mora: as listas, os mapas e o retrato que vai pro cliente.</summary>
	private sealed record SobraDaParada(
		int Caidas, bool ParedeAberta, bool VistaAberta, bool ArvoreDePe, int Tocos, int Rochas, bool RochaNoMapa, bool MobiliaDePe,
		int PortasAbertas, bool PortaAbertaNoMapa, bool MundoCarregado, bool MontanhaAberta, int Copias, bool InteriorAberto, int NoRetrato)
	{
		/// <summary>Tudo o que a bancada fez esta la.</summary>
		public bool Tudo => Caidas >= 5 && ParedeAberta && VistaAberta && !ArvoreDePe && Tocos >= 1 && Rochas >= 1 && RochaNoMapa && !MobiliaDePe
							&& PortasAbertas >= 1 && PortaAbertaNoMapa && MundoCarregado && MontanhaAberta && Copias >= 1 && InteriorAberto && NoRetrato >= 3;

		/// <summary>O servidor esta como o boot deixa.</summary>
		public bool Nada => Caidas == 0 && !ParedeAberta && !VistaAberta && ArvoreDePe && Tocos == 0 && Rochas == 0 && !RochaNoMapa && MobiliaDePe
							&& PortasAbertas == 0 && !PortaAbertaNoMapa && !MundoCarregado && !MontanhaAberta && Copias == 0 && !InteriorAberto && NoRetrato == 0;

		public string Texto =>
			$"{Caidas} celula(s) na lista; na Terra a parede {(ParedeAberta ? "ABERTA" : "de pe")} no mapa que barra e {(VistaAberta ? "ABERTA" : "de pe")} no que cega, "
			+ $"a arvore {(ArvoreDePe ? "de pe" : "CAIDA")}, {Tocos} toco(s), {Rochas} rocha(s){(RochaNoMapa ? " (barrando no mapa)" : "")}, a mobilia {(MobiliaDePe ? "de pe" : "CAIDA")}, "
			+ $"{NoRetrato} buraco(s) no retrato de quem entra; {PortasAbertas} porta(s) aberta(s){(PortaAbertaNoMapa ? " (aberta no mapa)" : "")}; "
			+ $"o mundo sorteado {(MundoCarregado ? "CARREGADO" : "fora da memoria")}{(MontanhaAberta ? " com a montanha ABERTA" : "")}; "
			+ $"{Copias} copia(s) de interior{(InteriorAberto ? " (a da nave com a parede ABERTA)" : "")}";
	}

	/// <summary>Derruba, pelo `DerrubarCelula`, a primeira parede desta zona que pode cair e que <paramref name="serve"/> aprova.</summary>
	private (int X, int Y)? DerrubarUmaDaParada(ZoneKey zona, ZoneCollision mapa, Func<int, int, bool> serve)
	{
		for (int y = 0; y < mapa.Height; y++)
			for (int x = 0; x < mapa.Width; x++)
				if (mapa.BloqueadaNoArquivo(x, y) && !mapa.Indestrutivel(x, y) && !mapa.NaBorda(x, y) && serve(x, y) && DerrubarCelula(zona, x, y))
					return (x, y);
		return null;
	}

	/// <summary>
	/// MONTA O ESTRAGO pelos caminhos de producao: na TERRA (zona de arquivo) uma parede, uma arvore (que deixa toco),
	/// uma rocha de minerio de pe e uma mobilia do mapa derrubada; uma PORTA aberta numa zona de arquivo; uma
	/// montanha derrubada num MUNDO SORTEADO (gerado de verdade); e uma parede derrubada no INTERIOR de uma nave.
	/// </summary>
	private EstragoDaParada? MontarOEstragoDaParada(ZoneKey terra, out string porQue)
	{
		porQue = "";
		if (MapaDaZonaOuCatalogo(terra) is not { } mapa) { porQue = "a Terra nao tem mapa"; return null; }
		HashSet<(int, int)> portasDaTerra = _portasDoMapa.TryGetValue(terra.Name, out List<PortaDoMapa>? daqui) ? [.. daqui.Select(p => (p.X, p.Y))] : [];

		(int X, int Y)? arvore = DerrubarUmaDaParada(terra, mapa, (x, y) => ArvoreDoMapaDePe(terra, mapa, x, y));
		(int X, int Y)? parede = DerrubarUmaDaParada(terra, mapa, (x, y) => !ArvoreDoMapaDePe(terra, mapa, x, y) && !portasDaTerra.Contains((x, y))
																			 && ObraNaCelula(terra, x, y) == null);

		(int X, int Y)? rocha = null;
		if (CampoDe(terra, mapa) is { } campo && (campo.DePe.Count > 0 || NascerRocha(campo, anunciar: false))) rocha = campo.DePe.First();

		(int X, int Y)? mobilia = null;
		Obra? movel = _noChao.FirstOrDefault(o => o.DoMapa && o.Zona.Equals(terra) && o.Tipo != TipoDaComida && _obras?.Get(o.Tipo) is { Densa: true });
		if (movel != null)
		{
			(int X, int Y) celula = Jandirus.Core.Tech.CatalogoDeObras.Celula(movel.X, movel.Y);
			Estragar(movel, 1e18, null);
			if (!_noChao.Contains(movel)) mobilia = celula;
		}

		(string? zonaDaPorta, PortaDoMapa porta) = _portasDoMapa
			.Where(kv => kv.Value.Count > 0 && _catalogo?.Get(kv.Key)?.Mapa != null)
			.OrderBy(kv => kv.Key, StringComparer.Ordinal)
			.Select(kv => ((string?)kv.Key, kv.Value[0])).FirstOrDefault();
		// o prazo da porta vai longe de proposito: ela nao pode fechar pelo relogio no meio da medicao
		if (zonaDaPorta != null) AbrirPorta(zonaDaPorta, porta.X, porta.Y, NowMs() + 3_600_000);

		// O MUNDO SORTEADO: o menor dos que ficam perto da Terra, gerado pelo caminho de producao.
		PlanetaNoEspaco? menor = null;
		int lado = int.MaxValue;
		for (int sy = -6; sy <= 6; sy++)
			for (int sx = -6; sx <= 6; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					PlanetaNoEspaco p = s.Planeta(k);
					int l = MundoProcedural.DaSeed(p.Seed, p.Nome).Lado;
					if (l < lado) { lado = l; menor = p; }
				}
			}
		(int X, int Y)? montanha = null;
		ZoneKey mundo = default;
		if (menor is { } planeta && SlAcordar(planeta) is { } viva)
		{
			mundo = Espaco.ZonaDe(planeta);
			montanha = DerrubarUmaDaParada(mundo, viva.Colisao, (_, _) => true);
		}

		ZoneKey interior = Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(IdBaseDaVigia + 500);
		(int X, int Y)? doInterior = MapaDaZonaOuCatalogo(interior) is { } daNave ? DerrubarUmaDaParada(interior, daNave, (_, _) => true) : null;

		if (arvore is not { } a || parede is not { } pa || rocha is not { } r || mobilia is not { } m || zonaDaPorta == null
			|| menor is not { } pl || montanha is not { } mo || doInterior is not { } di)
		{
			porQue = $"arvore {arvore}, parede {parede}, rocha {rocha}, mobilia {mobilia}, porta em {zonaDaPorta ?? "nenhuma"}, "
					 + $"mundo {(menor == null ? "nenhum" : "achado")}, montanha {montanha}, interior {doInterior}";
			return null;
		}
		return new EstragoDaParada(terra, pa, a, r, m, zonaDaPorta, (porta.X, porta.Y), pl, mundo, mo, interior, di);
	}

	/// <summary>Le o que ha do estrago agora -- SEM criar nada (nao pergunta pelo mapa do interior nem acorda o mundo).</summary>
	private SobraDaParada MedirOEstragoDaParada(EstragoDaParada p)
	{
		int copias = _interiores.Count;
		bool interiorAberto = _interiores.TryGetValue(p.Interior.Hash, out InteriorVivo? viva) && viva.Colisao is { } daNave
							  && !daNave.BlockedCell(p.ParedeDoInterior.X, p.ParedeDoInterior.Y);
		bool mundoCarregado = _zonasGeradas.TryGetValue(p.Mundo.Hash, out ZonaGerada? gerada);
		bool montanhaAberta = gerada != null && !gerada.Colisao.BlockedCell(p.Montanha.X, p.Montanha.Y);
		ZoneCollision? mapa = _catalogo?.Get(p.Terra)?.Mapa, vista = _catalogo?.Get(p.Terra)?.Vista;
		return new SobraDaParada(
			Caidas: _cenarioCaido.Values.Sum(c => c.Count),
			ParedeAberta: mapa != null && !mapa.BlockedCell(p.Parede.X, p.Parede.Y),
			VistaAberta: vista != null && vista.Aberta(p.Parede.X, p.Parede.Y),
			ArvoreDePe: mapa != null && ArvoreDoMapaDePe(p.Terra, mapa, p.Arvore.X, p.Arvore.Y),
			Tocos: _lenha.Values.Sum(z => z.Tocos.Count),
			Rochas: _rochas.Values.Sum(c => c.DePe.Count),
			RochaNoMapa: mapa != null && mapa.TemRocha(p.Rocha.X, p.Rocha.Y),
			MobiliaDePe: ObraNaCelula(p.Terra, p.Mobilia.X, p.Mobilia.Y) != null,
			PortasAbertas: _portasAbertas.Values.Sum(d => d.Count),
			PortaAbertaNoMapa: _catalogo?.Get(p.ZonaDaPorta)?.Mapa is { } daPorta && !daPorta.BlockedCell(p.Porta.X, p.Porta.Y),
			MundoCarregado: mundoCarregado,
			MontanhaAberta: montanhaAberta,
			Copias: copias,
			InteriorAberto: interiorAberto,
			NoRetrato: LerPacoteDeCenario(PacoteDeRetratoDeCenario(p.Terra).CopyData()).Celulas.Count);
	}

	public void RodarBancadaDaParada(int porta)
	{
		_paradaChamadas++;
		if (_paradaRodando || _paradaJaRodou) return;   // a subida que a propria bancada faz chega aqui de novo
		_paradaRodando = _paradaJaRodou = true;
		_pdOk = _pdFalhou = 0;
		GD.Print("[parada] ================ A PARADA DO SERVIDOR E A SUBIDA SEGUINTE NO MESMO PROCESSO (dono, 2026-10-11) ================");

		const string contaA = "bancada_parada_selecao", contaB = "bancada_parada_mundo", contaC = "bancada_parada_par";
		const string nomeB = "Paradeiro", nomeC = "Parceiro";
		const string jaConectada = "essa conta ja esta conectada";
		const float T = ZoneCollision.TileSize;
		var a = new ClienteCruDeTeste();   // fica na SELECAO na hora da parada
		var b = new ClienteCruDeTeste();   // entra no MUNDO
		var c = new ClienteCruDeTeste();   // entra no MUNDO: o par de B na fusao e no embate
		var d = new ClienteCruDeTeste();   // OUTRO soquete, pedindo a mesma conta de A
		var mesa = new MesaDeClientesCrus(this, porta, a, b, c, d);
		int subidas = 1;                   // a primeira e a do boot -- e ela que esta chamando esta bancada
		(double wo, double contagem, double intervalo) relogiosDoTorneio = (_torneioCfg.WoSegundos, _torneioCfg.ContagemSegundos, _torneioCfg.IntervaloSegundos);

		// `--paradaantiga`: a PARTE I com os defeitos ligados. As checagens dos consertos tem de reprovar.
		bool antiga = Array.IndexOf(OS.GetCmdlineArgs(), "--paradaantiga") >= 0;
		if (antiga)
			GD.Print("[parada] ATENCAO (--paradaantiga): a PARTE I roda com os DEFEITOS ligados -- este e o placar de ANTES dos consertos, e ele tem de sair VERMELHO");

		// ---------------------------------------------------------------- as pecas do roteiro
		void Defeito(bool naoDerruba = false, bool cobraAFusao = false, bool saidaDeAntes = false, bool lembra = false, bool lembraDoEstrago = false)
		{
			ParadaNaoDerrubaDeTeste = naoDerruba || antiga;
			ParadaCobraAFusaoDeTeste = cobraAFusao || antiga;
			SaidaDoEmbateDeAntesDeTeste = saidaDeAntes || antiga;
			ParadaLembraDaSessaoDeTeste = lembra || antiga;
			ParadaLembraDoEstragoDeTeste = lembraDoEstrago || antiga;
		}
		// `comoAnfitriao`: a subida de quem clicou em "Hospedar partida" (sorteia a prova desta hospedagem); sem
		// ele, a de um servidor dedicado -- que e tambem a do boot deste processo, e por isso a que a regua compara.
		bool Sobe(bool comoAnfitriao = false)
		{
			subidas++;
			return comoAnfitriao ? SubirComAnfitriao(porta) : Start(porta);
		}
		float Tiles(Vec2 de, Vec2 ate) => Vec2.Distance(de, ate) / T;
		Vec2 LugarNoDisco(CharacterSave s) => new(s.X, s.Y);
		bool Perto(double valor, double esperado) => Math.Abs(valor - esperado) <= 1e-6 * Math.Max(1, Math.Abs(esperado));
		bool Anfitriao(ClienteCruDeTeste cli, ServerPlayer? corpo) => corpo != null && EhHost(mesa.ParDe(cli)) && EhAdmin(corpo);
		b.ProvaAoDiscar = () => ProvaDoAnfitriaoPara("127.0.0.1", porta);   // B e o cliente "do mesmo processo": pergunta a prova ao servidor a cada discagem

		// O POVOAMENTO DE PRODUCAO, tique a tique, ate a fila de nascimento secar. Devolve quantos habitantes ha e
		// a digital de QUEM eles sao (planeta, molde e nome de cada um).
		(int Vivos, ulong Quem) Povoa()
		{
			int tiques = 0;
			do { TickDoPovoamento(Protocol.TickSeconds); tiques++; }
			while (_filaDoPovoamento.Count > 0 && tiques < TetoDeTiquesDoCensoDaParada);
			List<string> censo = [.. _players.Values.Where(p => EhNpcDoMundo(p) && !p.Ficha.dead)
											 .Select(p => $"{p.Berco.Natal}|{p.Papel!.Molde.Id}|{p.Name}")
											 .OrderBy(linha => linha, StringComparer.Ordinal)];
			return (censo.Count, Versao.Fnv1a64(string.Join("\n", censo)));
		}

		// O PALCO DE DOIS: C na ancora, B a quatro tiles, na mesma zona. Devolve se os dois ficaram onde deviam.
		ZoneKey terra = default;
		Vec2 ancora = default;
		bool Posiciona(ServerPlayer? corpoB, ServerPlayer? corpoC)
		{
			if (corpoB == null || corpoC == null || MapaDaZonaOuCatalogo(terra) is not { } mapa) return false;
			Vec2 lugarC = mapa.PontoLivrePerto(ancora), lugarB = mapa.PontoLivrePerto(ancora + new Vec2(TilesEntreOsDoisDaParada * T, 0));
			foreach ((ServerPlayer corpo, Vec2 lugar) in new[] { (corpoC, lugarC), (corpoB, lugarB) })
			{
				if (corpo.Zone.Hash != terra.Hash) MoveToZone(corpo.Id, terra, lugar);
				CravarPosicao(corpo, lugar);
			}
			TickDaSalaDoTempo();   // quem a bancada tirou da sala deixa de ocupar vaga pelo tique de producao
			return corpoB.Zone.Hash == terra.Hash && corpoC.Zone.Hash == terra.Hash && Tiles(corpoB.Pos, corpoC.Pos) >= TilesEntreOsDoisDaParada - 1;
		}
		bool Funde(ServerPlayer? dono, ServerPlayer? passageiro)
		{
			if (dono == null || passageiro == null) return false;
			Fundir(dono, passageiro, TipoDeFusao.Danca, estragada: false);
			return EstaFundido(dono.Id) && EstaFundido(passageiro.Id) && EhOSelo(passageiro.Zone);
		}
		bool Embate(ServerPlayer? corpoB, ServerPlayer? corpoC)
		{
			if (corpoB == null || corpoC == null) return false;
			Comecar(corpoB, corpoC, NowMs());
			return _emEmbate.ContainsKey(corpoB.Id) && _emEmbate.ContainsKey(corpoC.Id) && corpoB.Ficha.isconcealed && corpoC.Ficha.isconcealed;
		}
		// Quem acorda DENTRO da sala ocupa vaga pelo `SalaDoTempoNoLogin` de producao; a bancada so o leva ate la --
		// e deixa nele a MARCA que a porta deixa (`SalaPlaneta`): sem ela o login nao da vaga a quem nunca entrou
		// pela porta (ver `GameServer.SalaPresenca.cs`).
		bool PoeNaSala(ServerPlayer? corpo)
		{
			var sala = ZoneKey.Premade(ZonaDaSala);
			if (corpo == null || _catalogo?.Get(sala) is not { } ficha) return false;
			MoveToZone(corpo.Id, sala, new Vec2((EntradaByondX - 1) * T + T / 2f, (ficha.H - EntradaByondY) * T + T / 2f));
			corpo.SalaPlaneta = Jandirus.Core.World.SalaDoTempo.PortaDaTerra;
			SalaDoTempoNoLogin(corpo);
			return string.Equals(corpo.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase) && _naSala.ContainsKey(corpo.Assinatura);
		}
		// UM TORNEIO DA TERRA EM DISPUTA, pelas portas de producao: abre, o lutador se inscreve, as inscricoes
		// fecham (a chave e sorteada, os convidados nascem, todo mundo vai preso pra area de espera).
		TorneioVivo? AbreTorneioCom(ServerPlayer? lutador)
		{
			if (lutador == null || _torneio != null) return null;
			ComecarTorneio(TipoDeTorneio.Terra, "a bancada da parada");
			Inscrever(lutador);
			if (_torneio is not { } aberto) return null;
			FecharInscricoes(aberto);
			return _torneio == aberto && aberto.Fase == FaseDoTorneio.Preparando && PresoNoTorneio(lutador.Id) ? aberto : null;
		}
		// O TIQUE DO TORNEIO de producao, pelo relogio de parede, ate uma luta ser decidida ou o prazo acabar.
		void TicaOTorneio(int tetoMs)
		{
			var relogio = System.Diagnostics.Stopwatch.StartNew();
			while (relogio.ElapsedMilliseconds < tetoMs && (ChaveDeTeste?.LutasDecididas ?? 0) == 0)
			{
				TickDoTorneio(Protocol.TickSeconds);
				Thread.Sleep(15);
			}
		}

		// OS CAMPOS QUE A PARADA PROMETE DEIXAR COMO O BOOT DEIXA -- as cinco colecoes de sessao, os estados que o
		// `Stop` rearma, o que a saida de cada um desfaz (fusoes e embates), e o ESTRAGO da sessao: o cenario caido,
		// as portas abertas, a lenha (tocos e rebrotas), as rochas de pe, as pecas no chao, a comida da Sala, os
		// mundos sorteados carregados e as copias de interior.
		string[] daSessao =
		[
			"_players", "_byPeer", "_logados", "_contas", "_zones",
			"_filaDoPovoamento", "_lugaresDoPovoamento", "_proximaManutencao", "_relogioDoPovoamento",
			"_provaDoAnfitriao", "_naSala", "_torneio",
			"_fusoes", "_fundidos", "_embates", "_emEmbate",
			"_cenarioCaido", "_portasAbertas", "_lenha", "_arvoresDoMundo", "_rochas", "_rochasVistas", "_pecasNoChao", "_pecasContadas",
			"_comidaPosta", "_zonasGeradas", "_interiores", "_interioresPorChave", "_chavesDeMundoSorteado",
		];
		string DiferencasDaSessao(SortedDictionary<string, string> doZero, SortedDictionary<string, string> agora) =>
			string.Join("; ", daSessao.Where(campo => !doZero.TryGetValue(campo, out string? antes) || !agora.TryGetValue(campo, out string? depois) || antes != depois)
									  .Select(campo => $"{campo}: {doZero.GetValueOrDefault(campo, "(nao existe)")} -> {agora.GetValueOrDefault(campo, "(nao existe)")}"));

		try
		{
			_torneioCfg.WoSegundos = WoDoTorneioDaParada;
			_torneioCfg.ContagemSegundos = ContagemDoTorneioDaParada;
			_torneioCfg.IntervaloSegundos = IntervaloDoTorneioDaParada;

			// =====================================================================================================
			// PARTE I -- o caminho de quem hospeda, sem defeito nenhum
			// =====================================================================================================
			GD.Print("[parada] -- 1) a primeira subida (a do boot): o mundo se povoa, um na SELECAO, dois no MUNDO, FUNDIDOS na parada --");
			SortedDictionary<string, string> retratoDoZero = RetratoDoServidorDeTeste();
			AfirmarPd("(montagem) a porta esta aberta e este servidor acabou de subir: ninguem logado, nenhum corpo, a sala vazia, nenhum torneio, nenhuma prova de anfitriao (ninguem hospedou: e a subida do `--server`)",
					  Running && _logados.Count == 0 && _contas.Count == 0 && _players.Count == 0 && QuantosNaSala() == 0 && !TorneioAtivo
					  && _provaDoAnfitriao == null,
					  $"{_players.Count} corpo(s), {_logados.Count} na selecao, {_contas.Count} no mundo");
			(int vivosDoZero, ulong quemDoZero) = Povoa();
			AfirmarPd("(montagem) a primeira volta do tique POVOA o mundo -- no boot ninguem espera manutencao nenhuma",
					  vivosDoZero > 0 && _filaDoPovoamento.Count == 0, $"{vivosDoZero} habitante(s)");

			bool aEntrou = mesa.Entra(a) && mesa.Loga(a, contaA);
			AfirmarPd("A passa pelo aperto de mao, loga e fica na SELECAO: a conta dele esta em `_logados`",
					  aEntrou && a.Slots != null && a.Recusa == null && _logados.Values.Count(v => v.Conta == contaA) == 1, a.Recusa ?? "");

			ServerPlayer? corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			AfirmarPd("C cria um personagem e entra no MUNDO, sozinho e da maquina do servidor -- e num servidor que ninguem hospedou isso nao da admin a ninguem",
					  corpoC != null && !EhAdmin(corpoC) && _contas.Values.Count(v => v.Conta == contaC) == 1, c.Recusa ?? "");
			ServerPlayer? corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			AfirmarPd("(montagem) B entra tambem: dois no mundo, um na selecao",
					  corpoB != null && corpoC != null && !EhAdmin(corpoB) && Jogadores.Count() == 2 && _logados.Count == 1, b.Recusa ?? "");
			ClienteCruDeTeste[] quatro = [a, b, c, d];
			AfirmarPd("(montagem) os quatro clientes discam de soquetes diferentes (portas locais distintas)",
					  quatro.Select(cli => cli.PortaLocal).Distinct().Count() == quatro.Length, string.Join(", ", quatro.Select(cli => cli.PortaLocal)));

			if (corpoC != null)
			{
				terra = corpoC.Zone;
				ancora = PontoDeNascimento(terra);
			}
			double ganho = corpoB == null ? 1 : Math.Max(1, Math.Round(corpoB.Ficha.BP * 0.25));

			bool palco = Posiciona(corpoB, corpoC);
			double bpDoZero = corpoB?.Ficha.BP ?? 0, buffDoZero = corpoB?.Ficha.FuseBuff ?? 0;
			double[] statsDoZero = corpoB == null ? [] : StatsDe(corpoB.Ficha);
			bool fundiu = Funde(corpoB, corpoC);
			if (corpoB != null) corpoB.Ficha.BP += ganho;
			int semDono = _players.Values.Count(EhNpcDoMundo);
			AfirmarPd("(montagem) na hora da parada: A na selecao, B e C FUNDIDOS (C no selo), e o mundo cheio de corpos sem dono",
					  palco && fundiu && _logados.Count == 1 && Jogadores.Count() == 2 && semDono == vivosDoZero && _fusoes.Count == 1,
					  $"{Jogadores.Count()} jogador(es), {semDono} habitante(s), {_fusoes.Count} fusao(oes)");

			Defeito();
			SalvarEParar();
			AfirmarPd("**A PARADA DERRUBA CADA CONECTADO E ESQUECE A SESSAO**: nao sobra conta na selecao nem no mundo, nem corpo (os habitantes somem junto, como sempre), par ou zona -- e a fusao foi DESFEITA pela saida, nao esquecida de pe",
					  !Running && _logados.Count == 0 && _contas.Count == 0 && _players.Count == 0 && _byPeer.Count == 0 && _zones.Count == 0
					  && _fusoes.Count == 0 && _fundidos.Count == 0 && ContaViva(contaA) == null && ContaViva(contaB) == null && ContaViva(contaC) == null,
					  $"{_logados.Count} na selecao, {_contas.Count} no mundo, {_players.Count} corpo(s), {_fusoes.Count} fusao(oes)");
			CharacterSave? paradaB = mesa.NoDisco(contaB), paradaC = mesa.NoDisco(contaC);
			AfirmarPd("**4. OS DOIS FUNDIDOS SAO GRAVADOS NA PARADA, COMO PESSOAS**: o dono com o treino feito DURANTE a fusao, sem o buff e com os stats dele; o passageiro ao lado do dono, fora do selo",
					  paradaB != null && paradaC != null
					  && Perto(paradaB.Ficha.BP, bpDoZero + ganho) && Perto(paradaB.Ficha.FuseBuff, buffDoZero) && StatsDe(paradaB.Ficha).SequenceEqual(statsDoZero)
					  && paradaC.Zona == paradaB.Zona && paradaC.Zona != NomeDoSelo && Tiles(LugarNoDisco(paradaC), LugarNoDisco(paradaB)) <= 2,
					  paradaB == null || paradaC == null ? "sem save" : $"BP {bpDoZero:0.##} + {ganho:0.##} -> {paradaB.Ficha.BP:0.##}; passageiro em {paradaC.Zona}");
			AfirmarPd("**A FUSAO E PERDOADA NA PARADA**: foi o servidor que desfez, e nenhum dos dois paga a recarga de 1 h",
					  paradaB != null && paradaC != null && paradaB.Ficha.fusion_cooldown_until == 0 && paradaC.Ficha.fusion_cooldown_until == 0,
					  $"recarga no disco: dono {paradaB?.Ficha.fusion_cooldown_until}, passageiro {paradaC?.Ficha.fusion_cooldown_until}");
			mesa.Bombeia(() => a.Caiu != null && b.Caiu != null && c.Caiu != null, 4000);
			AfirmarPd("os tres clientes recebem a despedida pelo fio (o servidor encerrou a conexao)",
					  new[] { a, b, c }.All(cli => cli.Caiu == nameof(DisconnectReason.RemoteConnectionClose)),
					  $"A: {a.Caiu ?? "nada"}, B: {b.Caiu ?? "nada"}, C: {c.Caiu ?? "nada"}");

			// ---------------------------------------------------------------- 2. a subida seguinte
			GD.Print("[parada] -- 2) a subida seguinte: o retrato, o mundo povoado, a mesma conta de novo, o CONTROLE da fusao; B na SALA DO TEMPO e C num TORNEIO --");
			AfirmarPd("(montagem) a subida de producao abre a porta de novo, no MESMO processo", Sobe() && Running);
			AfirmarPd("A REENTRADA: a segunda subida releu a linha de comando, chegou nesta bancada e foi mandada embora (ela nao roda dentro de si mesma)",
					  _paradaChamadas == subidas, $"{_paradaChamadas} chamada(s) em {subidas} subida(s)");
			string diferencas = DiferencasDaSessao(retratoDoZero, RetratoDoServidorDeTeste());
			AfirmarPd("**A REGUA**: parar e subir deixa a sessao como o boot deixa -- as cinco colecoes de sessao, o povoamento, a prova de anfitriao, a sala, o torneio, as fusoes e os embates batem com o retrato de quem subiu do zero",
					  diferencas.Length == 0, diferencas);
			(int vivos2, ulong quem2) = Povoa();
			AfirmarPd("**1. O MUNDO DA SUBIDA SEGUINTE NASCE POVOADO**: na primeira volta do tique, sem esperar a manutencao de cinco minutos -- e com os MESMOS habitantes do boot",
					  vivos2 == vivosDoZero && quem2 == quemDoZero, $"{vivos2} habitante(s) agora, {vivosDoZero} no boot");

			bool aVoltou = mesa.Entra(a);
			NetPeer? parDaVolta = mesa.ParDe(a);
			bool aLogou = mesa.Loga(a, contaA);
			AfirmarPd("**A MESMA CONTA LOGA DE NOVO E E ACEITA**: A volta do mesmo soquete, nao ha chave velha respondendo por ele, a lista de slots chega sem recusa",
					  aVoltou && parDaVolta != null && aLogou && a.Slots != null && a.Recusa == null && _logados.Values.Count(v => v.Conta == contaA) == 1,
					  a.Recusa ?? "");
			bool dTentou = mesa.Entra(d) && mesa.Loga(d, contaA);
			AfirmarPd($"...e quem ESTA MESMO conectado continua protegido: a segunda tela da mesma conta ainda ouve \"{jaConectada}\"",
					  dTentou && d.Slots == null && d.Recusa == jaConectada, d.Recusa ?? "(sem resposta)");

			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			AfirmarPd("...e B achou o personagem GRAVADO pela parada: uma copia viva da conta, a desta sessao (a que o `Persistir` vai gravar)",
					  corpoB != null && mesa.ParDe(b) is { } parDeB && _contas.TryGetValue(parDeB, out AccountSave? viva) && ReferenceEquals(ContaViva(contaB), viva)
					  && !EstaFundido(corpoB.Id) && Perto(corpoB.Ficha.BP, paradaB?.Ficha.BP ?? -1));

			// O CONTROLE DA FUSAO: os dois fundem de novo e SAEM DO JOGO pelo `Drop` de producao.
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			palco = Posiciona(corpoB, corpoC);
			fundiu = Funde(corpoB, corpoC);
			if (corpoB != null) corpoB.Ficha.BP += ganho;
			bool sairam = mesa.SaiDoJogo(b, contaB) && mesa.SaiDoJogo(c, contaC);
			CharacterSave? saidaB = mesa.NoDisco(contaB), saidaC = mesa.NoDisco(contaC);
			long agora = NowMs();
			AfirmarPd("**O CONTROLE: NO LOGOUT NORMAL A RECARGA DA FUSAO CONTINUA SENDO COBRADA** -- os dois fundidos saem do jogo pelo `Drop`, a fusao se separa e os dois pagam a hora",
					  palco && fundiu && sairam && saidaB != null && saidaC != null
					  && saidaB.Ficha.fusion_cooldown_until > agora && saidaC.Ficha.fusion_cooldown_until > agora,
					  saidaB == null ? "sem save" : $"recarga do dono vence em {(saidaB.Ficha.fusion_cooldown_until - agora) / 60000.0:0.#} min");
			AfirmarPd("...e fora a recarga, o que a saida normal grava e o MESMO que a parada gravou: o mesmo ganho em cima do save, sem o buff, com os stats dele, o passageiro ao lado e fora do selo",
					  paradaB != null && saidaB != null && saidaC != null
					  && Perto(saidaB.Ficha.BP, paradaB.Ficha.BP + ganho) && Perto(saidaB.Ficha.FuseBuff, paradaB.Ficha.FuseBuff)
					  && StatsDe(saidaB.Ficha).SequenceEqual(StatsDe(paradaB.Ficha))
					  && saidaC.Zona == saidaB.Zona && saidaC.Zona != NomeDoSelo && Tiles(LugarNoDisco(saidaC), LugarNoDisco(saidaB)) <= 2,
					  paradaB == null || saidaB == null ? "sem save" : $"BP {paradaB.Ficha.BP:0.##} + {ganho:0.##} -> {saidaB.Ficha.BP:0.##}");

			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			bool naSala = PoeNaSala(corpoB);
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			TorneioVivo? torneio = AbreTorneioCom(corpoC);
			Vec2 lugarNaEspera = corpoC?.Pos ?? default;
			AfirmarPd("(montagem) na hora da parada: B DENTRO da Sala do Tempo, ocupando uma vaga, e C inscrito num TORNEIO em disputa -- a chave sorteada, os convidados no palco, ele preso na area de espera",
					  naSala && QuantosNaSala() == 1 && torneio != null && TorneioAtivo && corpoC != null && corpoC.Combate.Carencia >= CarenciaDeEspera,
					  $"{QuantosNaSala()} na sala; torneio em {FaseDoTorneioDeTeste}, {NpcsDoTorneioDeTeste.Count} convidado(s)");

			Defeito();
			SalvarEParar();
			AfirmarPd("a parada grava B dentro da sala (e pra la que ele volta) e a sala fica sem ninguem ocupando vaga",
					  !Running && mesa.NoDisco(contaB)?.Zona == ZonaDaSala && QuantosNaSala() == 0, $"B no disco em {mesa.NoDisco(contaB)?.Zona ?? "nada"}; {QuantosNaSala()} na sala");
			CharacterSave? noPalco = mesa.NoDisco(contaC);
			AfirmarPd("**O TORNEIO E CANCELADO NA PARADA**: nao sobra torneio na memoria, nenhuma luta foi decidida (ninguem perdeu por W.O.), e quem estava no palco foi gravado ONDE ESTAVA",
					  !TorneioAtivo && torneio != null && torneio.LutasDisputadas == 0 && noPalco != null && Tiles(LugarNoDisco(noPalco), lugarNaEspera) < 0.05f,
					  $"torneio na memoria: {FaseDoTorneioDeTeste}; lutas decididas: {torneio?.LutasDisputadas}; C no disco em {noPalco?.Zona ?? "nada"}");

			// ---------------------------------------------------------------- 3. a sala, o torneio e o admin; o embate
			GD.Print("[parada] -- 3) a terceira subida, HOSPEDADA: a sala vazia, sem torneio, quem hospeda e admin pela prova; B e C num ZANZOCLASH na parada --");
			AfirmarPd("(montagem) a porta sobe de novo, desta vez pela chamada de quem clica em \"Hospedar partida\"", Sobe(comoAnfitriao: true) && Running);
			byte[]? provaDaTerceira = ProvaDoAnfitriaoPara("127.0.0.1", porta)?.ToArray();
			AfirmarPd("**3. A SUBIDA SEGUINTE COMECA COM A SALA DO TEMPO VAZIA**: ninguem conectado, nenhuma vaga ocupada", QuantosNaSala() == 0, $"{QuantosNaSala()} na sala");
			TicaOTorneio(400);
			AfirmarPd("**...E SEM TORNEIO**: o tique do torneio roda e nao ha nada em disputa nem luta decidida por ausencia (o proximo e o da agenda, que foi marcada quando aquele comecou)",
					  !TorneioAtivo, FaseDoTorneioDeTeste);
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			AfirmarPd("...e NADA DO TORNEIO VEIO DO SAVE: C acorda no palco, onde estava, LIVRE -- sem a trava da area de espera e podendo ser tocado",
					  corpoC != null && !PresoNoTorneio(corpoC.Id) && corpoC.Combate.Carencia < CarenciaDeEspera && Tiles(corpoC.Pos, lugarNaEspera) < 0.05f,
					  corpoC == null ? "sem corpo" : $"carencia {corpoC.Combate.Carencia:0.##}");
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			AfirmarPd("...e a vaga de quem estava na sala VOLTA SOZINHA NO LOGIN: B acorda la dentro e ocupa uma (o `SalaDoTempoNoLogin` de producao)",
					  corpoB != null && string.Equals(corpoB.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase) && QuantosNaSala() == 1,
					  $"B em {corpoB?.Zone.Name ?? "nada"}; {QuantosNaSala()} na sala");
			AfirmarPd("**2. QUEM HOSPEDA E ADMIN, E SO ELE**: B, o cliente do mesmo processo, levou a prova desta hospedagem no aperto de mao; C, que entrou antes e do mesmo computador, e um jogador comum",
					  provaDaTerceira != null && Anfitriao(b, corpoB) && corpoC != null && !EhAdmin(corpoC) && !EhHost(mesa.ParDe(c)));

			palco = Posiciona(corpoB, corpoC);
			bool emEmbate = Embate(corpoB, corpoC);
			Vec2 lugarB = corpoB?.Pos ?? default, lugarC = corpoC?.Pos ?? default;
			Dictionary<string, double[]> corpoDeB = corpoB == null ? [] : FotografarCorpo(corpoB.Combate), corpoDeC = corpoC == null ? [] : FotografarCorpo(corpoC.Combate);
			AfirmarPd("(montagem) na hora da parada B e C estao num ZANZOCLASH: ocultos e travados, a quatro tiles um do outro, e a sala voltou a ficar vazia pelo tique de producao",
					  palco && emEmbate && _embates.Count == 1 && QuantosNaSala() == 0, $"{Tiles(lugarB, lugarC):0.#} tile(s) entre os dois; {QuantosNaSala()} na sala");

			Defeito();
			SalvarEParar();
			CharacterSave? embateB = mesa.NoDisco(contaB), embateC = mesa.NoDisco(contaC);
			AfirmarPd("**A PARADA NAO CASTIGA NINGUEM**: o embate se desfaz SEM vencedor -- os dois vao pro disco ONDE ESTAVAM (ninguem foi recolocado atras do outro), com o corpo como estava (ninguem levou o golpe de saida) e VISIVEIS (o embate terminou antes do save)",
					  !Running && _embates.Count == 0 && _emEmbate.Count == 0 && embateB != null && embateC != null
					  && Tiles(LugarNoDisco(embateB), lugarB) < 0.05f && Tiles(LugarNoDisco(embateC), lugarC) < 0.05f
					  && !embateB.Ficha.isconcealed && !embateC.Ficha.isconcealed
					  && MesmosMembrosDeTeste(embateB.Membros, corpoDeB) && MesmosMembrosDeTeste(embateC.Membros, corpoDeC),
					  embateB == null || embateC == null ? "sem save"
						  : $"B a {Tiles(LugarNoDisco(embateB), lugarB):0.##} tile(s) de onde estava e a {Tiles(LugarNoDisco(embateB), lugarC):0.#} de C; oculto no disco: B {embateB.Ficha.isconcealed}, C {embateC.Ficha.isconcealed}");

			// ---------------------------------------------------------------- 4. a regua geral
			GD.Print("[parada] -- 4) a quarta subida: A REGUA GERAL -- o retrato de quem subiu do zero contra o de depois de tres paradas de quem hospeda --");
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			SortedDictionary<string, string> retratoDepois = RetratoDoServidorDeTeste();
			diferencas = DiferencasDaSessao(retratoDoZero, retratoDepois);
			AfirmarPd("**A REGUA, DEPOIS DE TRES PARADAS** (fundidos, sala, torneio, embate, mundo povoado, uma hospedagem com anfitriao): a sessao continua batendo com o retrato de quem subiu do zero",
					  diferencas.Length == 0, diferencas);
			// O QUE AINDA DIFERE do boot, fora da sessao: so INFORMACAO pro relatorio -- nao e afirmacao, e a lista e a
			// deste roteiro (o que ele nao exercita nao aparece).
			List<string> sobras = [.. retratoDepois.Where(kv => retratoDoZero.GetValueOrDefault(kv.Key) != kv.Value
															   && !kv.Key.Contains("parada", StringComparison.OrdinalIgnoreCase) && !kv.Key.StartsWith("_pd", StringComparison.Ordinal))
													   .Select(kv => $"{kv.Key}: {retratoDoZero.GetValueOrDefault(kv.Key, "(nao existia)")} -> {kv.Value}")];
			GD.Print($"[parada]   SOBROU (informacao, nao afirmacao): {sobras.Count} de {retratoDepois.Count} campo(s) do servidor diferem do retrato do boot depois de tres paradas");
			foreach (string sobra in sobras.Take(TetoDeSobrasNoLogDaParada)) GD.Print($"[parada]   SOBROU   {sobra}");

			c.ProvaAoDiscar = () => provaDaTerceira;   // quem guardou a prova da hospedagem que acabou
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			AfirmarPd("**2. A PROVA DE QUEM HOSPEDAVA MORREU COM A PARADA**: o servidor subiu de novo sem anfitriao, nao entrega prova a ninguem, e nem quem guardou a da hospedagem anterior (C), nem quem hospedava (B), e admin",
					  provaDaTerceira != null && _provaDoAnfitriao == null && ProvaDoAnfitriaoPara("127.0.0.1", porta) == null
					  && corpoC != null && !EhAdmin(corpoC) && !EhHost(mesa.ParDe(c)) && corpoB != null && !EhAdmin(corpoB) && !EhHost(mesa.ParDe(b)));
			c.ProvaAoDiscar = null;
			bool sairamDaQuarta = mesa.SaiDoJogo(b, contaB) && mesa.SaiDoJogo(c, contaC);
			AfirmarPd("(montagem) os dois saem", sairamDaQuarta);

			// ---------------------------------------------------------------- 4b. o servidor dedicado
			GD.Print("[parada] -- 4b) o SERVIDOR DEDICADO: fechar a janela (ou o console) GRAVA tudo; a corrida de bancada, nao --");
			string[] doServidorBat = ["--headless", "--path", ".", "--server", "--port", "7777"];   // a linha do `servidor.bat`
			bool estaEBancada = !Jandirus.Client.Boot.EhDedicadoDeProducao(OS.GetCmdlineArgs(), out string porQueEsta);
			AfirmarPd("**O CRITERIO \"DEDICADO DE PRODUCAO x BANCADA\"**: a linha do `servidor.bat` e de producao (com ou sem `--admin <conta>`); ESTA corrida, as bancadas de servidor, o anfitriao-robo e um processo sem `--server` nao sao",
					  Jandirus.Client.Boot.EhDedicadoDeProducao(doServidorBat, out _)
					  && Jandirus.Client.Boot.EhDedicadoDeProducao(["--server"], out _)
					  && Jandirus.Client.Boot.EhDedicadoDeProducao([.. doServidorBat, FlagDoAdmin, "conta_do_dono"], out _)
					  && !Jandirus.Client.Boot.EhDedicadoDeProducao([.. doServidorBat, "--sem-admin-local"], out _)   // a flag aposentada virou argumento desconhecido
					  && estaEBancada
					  && !Jandirus.Client.Boot.EhDedicadoDeProducao(["--headless", "--path", ".", "--server", "--port", "7910", "--embatekiteste"], out _)
					  && !Jandirus.Client.Boot.EhDedicadoDeProducao(["--headless", "--path", ".", "--server", "--diagberco", "--bercovivo"], out _)
					  && !Jandirus.Client.Boot.EhDedicadoDeProducao(["--headless", "--host", "--rede", "7981", "--diagqueda"], out _)
					  && !Jandirus.Client.Boot.EhDedicadoDeProducao(["--headless", "--path", "."], out _),
					  $"esta corrida: {porQueEsta}");
			var vigia = new Jandirus.Client.VigiaDoConsole { Name = "VigiaDoConsoleDeBancada" };
			AddChild(vigia);
			int vigiados = Jandirus.Client.VigiaDoConsole.SinaisVigiados;
			RemoveChild(vigia);
			vigia.Free();
			AfirmarPd("(montagem) dentro deste processo o vigia do console consegue se registrar nos quatro sinais (Ctrl+C, Ctrl+Break, fechar a janela, desligar) -- e solta os quatro ao sair da arvore",
					  vigiados == 4 && Jandirus.Client.VigiaDoConsole.SinaisVigiados == 0, $"{vigiados} sinal(is) vigiado(s)");

			// Uma rodada: B no mundo com zeni que ainda nao foi pro disco, e a PORTA DE SAIDA do processo. Devolve o
			// zeni que ficou no disco menos o que ja estava la -- a moeda inteira se a saida gravou, zero se nao.
			const double moedaDoDedicado = 321;
			double FechaComBNoMundo(Action fechar)
			{
				ServerPlayer? noMundo = mesa.EntraNoMundo(b, contaB, nomeB);
				if (noMundo == null) return double.NaN;
				double jaTinha = mesa.NoDisco(contaB)?.Ficha.Zeni ?? double.NaN;
				noMundo.Ficha.Zeni += moedaDoDedicado;
				Jandirus.Client.Saida.RearmarDeTeste();
				fechar();
				mesa.Bombeia(() => b.Caiu != null, 4000);
				return (mesa.NoDisco(contaB)?.Ficha.Zeni ?? double.NaN) - jaTinha;
			}
			void PeloX() => Jandirus.Client.Saida.Encerrar(null, "bancada: o X da janela do servidor dedicado (sem matar o processo)");

			Jandirus.Client.Boot.DecidirSeEDedicadoDeProducao(doServidorBat);
			Jandirus.Client.Saida.DedicadoNaoGravaDeTeste = antiga;
			Defeito();
			double gravouPeloX = FechaComBNoMundo(PeloX);
			AfirmarPd("**FECHAR O SERVIDOR DEDICADO GRAVA TUDO**: a porta de saida do processo (a mesma do X da janela) passa pelo `SalvarEParar` -- quem estava conectado e derrubado e gravado, e o servidor desce",
					  Perto(gravouPeloX, moedaDoDedicado) && !Running && _contas.Count == 0 && _players.Count == 0,
					  $"zeni novo no disco: {gravouPeloX:0.##} de {moedaDoDedicado:0}");

			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			Jandirus.Client.VigiaDoConsole.RearmarDeTeste();
			bool atendeu = false, soltou = false;
			double gravouPeloConsole = FechaComBNoMundo(() =>
			{
				// O SINAL DO CONSOLE chega numa thread do sistema: ela pede e ESPERA; quem grava e a principal.
				Task<bool> doSistema = Task.Run(() => Jandirus.Client.VigiaDoConsole.Pedir("bancada: Ctrl+C no console (sem matar o processo)", 4000));
				var relogio = System.Diagnostics.Stopwatch.StartNew();
				while (!atendeu && relogio.ElapsedMilliseconds < 3000)
				{
					atendeu = Jandirus.Client.VigiaDoConsole.Atender(null);
					if (!atendeu) Thread.Sleep(5);
				}
				soltou = doSistema.Wait(5000) && doSistema.Result;
			});
			AfirmarPd("**O CONSOLE TAMBEM**: um pedido de fechamento vindo de OUTRA thread (como o Ctrl+C e o fechar da janela do console chegam) e atendido na principal, grava tudo pela mesma porta, e so entao solta quem pediu",
					  atendeu && soltou && Perto(gravouPeloConsole, moedaDoDedicado) && !Running,
					  $"atendido: {atendeu}; quem pediu foi solto depois da gravacao: {soltou}; zeni novo no disco: {gravouPeloConsole:0.##}");

			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			Jandirus.Client.Saida.DedicadoNaoGravaDeTeste = true;
			double semGravarNoX = FechaComBNoMundo(PeloX);
			Jandirus.Client.Saida.DedicadoNaoGravaDeTeste = false;
			AfirmarPd("(defeito injetado: o dedicado nao grava) **FECHAR O SERVIDOR DEDICADO LEVA O QUE NAO TINHA SIDO GRAVADO**: a saida cai no `Stop` sozinho e o zeni novo nao vai pro disco -- a regua sabe reprovar",
					  Perto(semGravarNoX, 0) && !Running, $"zeni novo no disco: {semGravarNoX:0.##} de {moedaDoDedicado:0}");

			Jandirus.Client.Boot.DecidirSeEDedicadoDeProducao(OS.GetCmdlineArgs());
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			double semGravarNaBancada = FechaComBNoMundo(PeloX);
			AfirmarPd("**E A CORRIDA DE BANCADA CONTINUA SEM GRAVAR**: com a linha de comando DESTE processo (que tem um argumento de bancada), a mesma porta de saida cai no `Stop` sozinho, como sempre",
					  !Jandirus.Client.Boot.DedicadoDeProducao && Perto(semGravarNaBancada, 0) && !Running, $"zeni novo no disco: {semGravarNaBancada:0.##}");
			Jandirus.Client.Saida.RearmarDeTeste();

			// =====================================================================================================
			// PARTE II -- os contra-exemplos: um botao por conserto, e cada regua tem de saber reprovar
			// =====================================================================================================
			GD.Print("[parada] -- 5) com o DEFEITO INJETADO: a parada que nao derruba ninguem (a de antes) e os dois FUNDIDOS --");
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			palco = Posiciona(corpoB, corpoC);
			CharacterSave? antesB = mesa.NoDisco(contaB), antesC = mesa.NoDisco(contaC);
			fundiu = Funde(corpoB, corpoC);
			if (corpoB != null) corpoB.Ficha.BP += ganho;
			Defeito(naoDerruba: true);
			SalvarEParar();
			CharacterSave? velhaB = mesa.NoDisco(contaB), velhaC = mesa.NoDisco(contaC);
			AfirmarPd("(defeito injetado: a parada nao derruba ninguem) **OS FUNDIDOS NAO VAO PRO DISCO**: o `Persistir` recusa os dois, o treino feito durante a fusao some (o BP do dono no disco e o de antes) e o passageiro continua gravado onde estava -- a regua sabe reprovar",
					  palco && fundiu && antesB != null && antesC != null && velhaB != null && velhaC != null
					  && Perto(velhaB.Ficha.BP, antesB.Ficha.BP)
					  && Tiles(LugarNoDisco(velhaC), LugarNoDisco(antesC)) < 0.05f && Tiles(LugarNoDisco(velhaC), LugarNoDisco(velhaB)) > 2,
					  antesB == null || velhaB == null ? "sem save" : $"BP no disco {velhaB.Ficha.BP:0.##} (antes da fusao {antesB.Ficha.BP:0.##}; o corpo tinha {antesB.Ficha.BP + ganho:0.##})");
			AfirmarPd("(defeito injetado) ...e a fusao fica de pe na memoria de um servidor parado, sem corpo nenhum", _fusoes.Count == 1 && _fundidos.Count == 2 && _players.Count == 0,
					  $"{_fusoes.Count} fusao(oes), {_players.Count} corpo(s)");
			foreach (FusaoAtiva orfa in _fusoes.ToList()) Separar(orfa, "arrumacao da bancada da parada");   // a bancada desfaz o que o defeito DELA deixou

			GD.Print("[parada] -- 6) com o DEFEITO INJETADO: a parada que cobra a recarga da fusao que ela mesma desfaz --");
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			palco = Posiciona(corpoB, corpoC);
			long recargaDeB = corpoB?.Ficha.fusion_cooldown_until ?? 0, recargaDeC = corpoC?.Ficha.fusion_cooldown_until ?? 0;
			fundiu = Funde(corpoB, corpoC);
			Defeito(cobraAFusao: true);
			SalvarEParar();
			CharacterSave? cobradaB = mesa.NoDisco(contaB), cobradaC = mesa.NoDisco(contaC);
			AfirmarPd("(defeito injetado: a parada cobra a fusao) **OS DOIS PAGAM A RECARGA DE 1 h POR UMA SEPARACAO QUE FOI O SERVIDOR QUE FEZ**: a recarga no disco e nova, dos dois -- a regua sabe reprovar",
					  palco && fundiu && cobradaB != null && cobradaC != null
					  && cobradaB.Ficha.fusion_cooldown_until > recargaDeB && cobradaC.Ficha.fusion_cooldown_until > recargaDeC
					  && cobradaB.Ficha.fusion_cooldown_until > NowMs() && cobradaC.Ficha.fusion_cooldown_until > NowMs(),
					  cobradaB == null ? "sem save" : $"recarga do dono vence em {(cobradaB.Ficha.fusion_cooldown_until - NowMs()) / 60000.0:0.#} min");

			GD.Print("[parada] -- 7) com o DEFEITO INJETADO: o `Stop` SOZINHO que nao esquece a sessao (o de antes de 2026-10-11) --");
			AfirmarPd("(montagem) a porta sobe de novo, HOSPEDADA", Sobe(comoAnfitriao: true) && Running);
			(int vivos7, ulong quem7) = Povoa();
			bool aNaSelecao = mesa.Entra(a) && mesa.Loga(a, contaA);
			NetPeer? parVelhoDeA = _logados.FirstOrDefault(kv => kv.Value.Conta == contaA).Key;
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			const double moedaDeC = 777, moedaDeB = 555;
			double zeniDeC = corpoC?.Ficha.Zeni ?? 0;
			if (corpoC != null) corpoC.Ficha.Zeni += moedaDeC;
			naSala = PoeNaSala(corpoC);
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			if (corpoB != null && corpoB.Zone.Hash != terra.Hash) MoveToZone(corpoB.Id, terra, ancora);
			torneio = AbreTorneioCom(corpoB);
			AfirmarPd("(montagem) o palco do `Stop` sozinho: o mundo povoado, A na selecao, C dentro da sala e com zeni que ainda nao foi pro disco, e B -- que hospeda, admin pela prova -- num torneio em disputa",
					  vivos7 == vivosDoZero && quem7 == quemDoZero && aNaSelecao && parVelhoDeA != null && Anfitriao(b, corpoB)
					  && naSala && QuantosNaSala() == 1 && torneio != null && TorneioAtivo, $"{vivos7} habitante(s), {QuantosNaSala()} na sala, torneio em {FaseDoTorneioDeTeste}");

			Defeito(lembra: true);
			Stop();
			AfirmarPd("(defeito injetado: o `Stop` nao esquece a sessao) a conta da SELECAO sobra em `_logados` e as do MUNDO em `_contas`, de um servidor que parou -- e a leitora de \"copia viva da conta\" ainda acha as tres",
					  !Running && _players.Count == 0 && _logados.Count == 1 && _contas.Count == 2 && ContaViva(contaA) != null && ContaViva(contaB) != null && ContaViva(contaC) != null,
					  $"{_logados.Count} na selecao, {_contas.Count} no mundo");
			AfirmarPd("**O `Stop` SOZINHO NAO GRAVA NINGUEM, DE PROPOSITO** (e o caminho das sessoes que nao sao de jogador): C nao foi gravado na sala nem com o zeni novo",
					  mesa.NoDisco(contaC) is { } semGravar && semGravar.Zona != ZonaDaSala && Perto(semGravar.Ficha.Zeni, zeniDeC),
					  $"C no disco em {mesa.NoDisco(contaC)?.Zona ?? "nada"}, zeni {mesa.NoDisco(contaC)?.Ficha.Zeni:0.##}");
			mesa.Bombeia(() => a.Caiu != null && b.Caiu != null && c.Caiu != null, 4000);

			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			(int vivos8, _) = Povoa();
			AfirmarPd("(defeito injetado) **1. O MUNDO SOBE VAZIO**: nenhum habitante depois da primeira volta do tique -- o relogio da manutencao continuou de onde estava, e a proxima so vem em minutos. A regua sabe reprovar",
					  vivos8 == 0 && _proximaManutencao - _relogioDoPovoamento > 60,
					  $"{vivos8} habitante(s); proxima manutencao em {_proximaManutencao - _relogioDoPovoamento:0} s");
			AfirmarPd("(defeito injetado) **3. A SALA COMECA OCUPADA POR QUEM NAO ESTA**: uma vaga tomada, e ninguem no mundo", QuantosNaSala() == 1 && !Jogadores.Any(),
					  $"{QuantosNaSala()} na sala, {Jogadores.Count()} jogador(es)");
			bool sobreviveu = TorneioAtivo;
			TicaOTorneio(4000);
			AfirmarPd("(defeito injetado) **O TORNEIO ATRAVESSA A PARADA E DECIDE LUTA POR AUSENCIA**: ele continua em disputa num servidor sem ninguem, e o tique fecha uma luta sem nenhum dos dois no palco. A regua sabe reprovar",
					  sobreviveu && TorneioAtivo && (ChaveDeTeste?.LutasDecididas ?? 0) > 0, $"torneio em {FaseDoTorneioDeTeste}; lutas decididas: {ChaveDeTeste?.LutasDecididas}");

			bool apertoDeMao = mesa.Entra(a);
			NetPeer? parNovoDeA = mesa.ParDe(a);
			AfirmarPd("**A CAUSA, MEDIDA**: A volta do MESMO soquete; o par novo e OUTRO objeto e e IGUAL ao velho (o `NetPeer` compara pelo endereco), e a chave velha de `_logados` responde por ele",
					  apertoDeMao && parVelhoDeA != null && parNovoDeA != null && !ReferenceEquals(parVelhoDeA, parNovoDeA)
					  && parVelhoDeA.Equals(parNovoDeA) && _logados.ContainsKey(parNovoDeA),
					  $"velho {parVelhoDeA}, novo {parNovoDeA}");
			bool respondeu = mesa.Loga(a, contaA, 1500);
			AfirmarPd("(defeito injetado) **A MESMA CONTA NAO LOGA DE NOVO, E O SERVIDOR NAO DIZ NADA**: o aperto de mao passou, o login foi mandado, e nao volta lista de slots nem recusa -- o \"conectando...\" pra sempre (o `return` mudo da 1a linha do `Login`). A regua sabe reprovar",
					  apertoDeMao && !respondeu && a.Slots == null && a.Recusa == null && a.Conectado,
					  $"respondeu={respondeu} slots={(a.Slots == null ? "nao" : "sim")} recusa='{a.Recusa}' conectado={a.Conectado}");
			bool dRespondeu = mesa.Entra(d) && mesa.Loga(d, contaA);
			AfirmarPd($"(defeito injetado) ...e de OUTRO soquete a mesma conta e recusada em voz alta, sem ninguem conectado nela: \"{jaConectada}\"",
					  dRespondeu && d.Slots == null && d.Recusa == jaConectada, d.Recusa ?? "(sem resposta)");

			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			AfirmarPd("(defeito injetado) **2. A PROVA DA HOSPEDAGEM ANTERIOR ATRAVESSA A PARADA**: o servidor subiu de novo SEM anfitriao, e quem tem a prova velha ainda e carimbado como quem hospeda e entra ADMIN. A regua sabe reprovar",
					  corpoB != null && _provaDoAnfitriao != null && EhHost(mesa.ParDe(b)) && EhAdmin(corpoB), b.Recusa ?? "");
			double zeniDeB = corpoB?.Ficha.Zeni ?? 0;
			if (corpoB != null) corpoB.Ficha.Zeni += moedaDeB;

			Defeito();
			Stop();
			AfirmarPd("**O `Stop` SOZINHO ESQUECE A SESSAO** (inclusive o que o `Stop` com defeito tinha deixado): ninguem logado, nenhum corpo, a sala vazia, o torneio cancelado e a prova de quem hospedava esquecida",
					  !Running && _logados.Count == 0 && _contas.Count == 0 && _players.Count == 0 && _byPeer.Count == 0 && QuantosNaSala() == 0 && !TorneioAtivo
					  && _provaDoAnfitriao == null && ContaViva(contaA) == null && ContaViva(contaB) == null && ContaViva(contaC) == null,
					  $"{_logados.Count} na selecao, {_contas.Count} no mundo, {QuantosNaSala()} na sala, torneio {FaseDoTorneioDeTeste}");
			AfirmarPd("...e continua sem gravar ninguem: o zeni novo de B nao foi pro disco", mesa.NoDisco(contaB) is { } semZeni && Perto(semZeni.Ficha.Zeni, zeniDeB),
					  $"zeni no disco {mesa.NoDisco(contaB)?.Ficha.Zeni:0.##}");

			// ---------------------------------------------------------------- 7b. o estrago da sessao
			GD.Print("[parada] -- 7b) O ESTRAGO DA SESSAO NAO ATRAVESSA A PARADA: parede, arvore, rocha e mobilia na Terra; porta aberta; montanha num mundo sorteado; parede no interior de uma nave --");
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			if (corpoB != null && corpoB.Zone.Hash != terra.Hash) MoveToZone(corpoB.Id, terra, ancora);
			EstragoDaParada? estrago = MontarOEstragoDaParada(terra, out string semPalco);
			AfirmarPd("(montagem) o palco do estrago, pelos caminhos de producao (`DerrubarCelula`, `Estragar`, `AbrirPorta`, `NascerRocha`, a geracao de um mundo)",
					  corpoB != null && estrago != null, semPalco);
			if (estrago != null)
			{
				// o retrato de cenario que o cliente recebe AO ENTRAR na Terra (o login, ou a troca de zona logo depois)
				int BuracosPraQuemEntra()
				{
					List<(int Para, byte[] Fio)> escuta = [];
					EscutaDeCenario = escuta;
					try
					{
						corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
						if (corpoB is not { } quemEntrou) return -1;
						if (quemEntrou.Zone.Hash != terra.Hash) MoveToZone(quemEntrou.Id, terra, ancora);
						return escuta.Where(r => r.Para == quemEntrou.Id).Select(r => LerPacoteDeCenario(r.Fio))
									 .Where(r => r.Modo == Protocol.CenarioRetrato && r.Zona == terra.Hash)
									 .Select(r => r.Celulas.Count).DefaultIfEmpty(-1).Last();
					}
					finally { EscutaDeCenario = null; }
				}

				// A OBRA DA SALA DO TEMPO E DADO DE DISCO, e nao estrago de sessao: a maquina assentada na Sala de um mundo
				// atravessa a parada (dono, 2026-10-11: "construcoes dentro da sala do tempo devem continuar la").
				ZoneKey salaDoMundo = SalaDePlaneta.Interior((ulong)(IdBaseDaVigia + 611));
				(int X, int Y)? chaoDaSala = MapaDaZonaOuCatalogo(salaDoMundo) is { } mapaDaSala ? IntrChaoLivre(mapaDaSala, null) : null;
				Obra? maquinaDaSala = chaoDaSala is { } ondeAssentou ? IntrAssentar(salaDoMundo, ondeAssentou).Posta : null;
				AfirmarPd("(montagem) uma maquina assentada na Sala do Tempo de um mundo, pelo `Posicionar`", maquinaDaSala != null && maquinaDaSala.Zona.Equals(salaDoMundo));

				SobraDaParada antesDaParada = MedirOEstragoDaParada(estrago);
				AfirmarPd("(montagem) na hora da parada o estrago esta todo la: na lista, nos mapas (o que barra e o que cega) e no retrato de quem entra",
						  antesDaParada.Tudo, antesDaParada.Texto);

				// O ESTADO DE ANTES: a parada que lembra do estrago.
				Defeito(lembraDoEstrago: true);
				SalvarEParar();
				AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
				SobraDaParada sobrou = MedirOEstragoDaParada(estrago);
				string reguaComDefeito = DiferencasDaSessao(retratoDoZero, RetratoDoServidorDeTeste());
				int buracosComDefeito = BuracosPraQuemEntra();
				GD.Print($"[parada]   MEDIDO (o que SOBRAVA depois de parar e subir): {sobrou.Texto}");
				AfirmarPd("(defeito injetado: a parada lembra do estrago) **O ESTADO DE ANTES**: depois de parar e subir, a parede continua caida -- na lista, no mapa que barra, no que cega e no "
						  + "retrato de quem entra --, o toco continua toco, a rocha e a porta continuam onde estavam, a mobilia continua caida, o mundo sorteado continua carregado com o buraco dele "
						  + "e a copia do interior continua viva. A regua sabe reprovar",
						  sobrou.Tudo && buracosComDefeito >= 3 && reguaComDefeito.Length > 0, $"{buracosComDefeito} buraco(s) pro cliente que entrou; regua: {reguaComDefeito}");

				// A PARADA DE PRODUCAO.
				Defeito();
				SalvarEParar();
				SobraDaParada parado = MedirOEstragoDaParada(estrago);
				AfirmarPd("**A PARADA ESQUECE O ESTRAGO DA SESSAO** (inclusive o que a parada com defeito tinha deixado), e O MAPA VOLTA INTEIRO JUNTO COM A LISTA: nada na lista, a parede e a "
						  + "arvore de pe no mapa que barra e no que cega, nenhum toco, nenhuma rocha, a mobilia de volta, a porta fechada, nenhum mundo sorteado na memoria, nenhuma copia de interior",
						  !Running && parado.Nada, parado.Texto);

				AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
				string reguaDoEstrago = DiferencasDaSessao(retratoDoZero, RetratoDoServidorDeTeste());
				AfirmarPd("**A REGUA, COM CENARIO QUEBRADO ANTES DA PARADA**: a subida seguinte bate com o retrato de quem subiu do zero -- o cenario caido, as portas, a lenha, as rochas, as pecas, "
						  + "a comida da Sala, os mundos sorteados e as copias de interior", reguaDoEstrago.Length == 0, reguaDoEstrago);
				int buracos = BuracosPraQuemEntra();
				AfirmarPd("**O CLIENTE QUE ENTRA NAO RECEBE BURACO**: o retrato de cenario da Terra chega vazio (pelo login de producao)", buracos == 0, $"{buracos} buraco(s)");

				// o mundo sorteado renasce da seed, e o interior da planta: sem o buraco, e sem nada na lista deles
				ZonaGerada? renascido = SlAcordar(estrago.Planeta);
				ZoneCollision? daNave = MapaDaZonaOuCatalogo(estrago.Interior);
				AfirmarPd("**O MUNDO SORTEADO E O INTERIOR RENASCEM INTEIROS**: a montanha de pe no mundo gerado de novo, a parede de pe na copia nova da nave, e nenhum dos dois na lista do estrago",
						  renascido != null && renascido.Colisao.BlockedCell(estrago.Montanha.X, estrago.Montanha.Y)
						  && daNave != null && daNave.BlockedCell(estrago.ParedeDoInterior.X, estrago.ParedeDoInterior.Y)
						  && !_cenarioCaido.ContainsKey(ChaveDeEstado(estrago.Mundo)) && !_cenarioCaido.ContainsKey(ChaveDeEstado(estrago.Interior))
						  && LerPacoteDeCenario(PacoteDeRetratoDeCenario(estrago.Mundo).CopyData()).Celulas.Count == 0
						  && LerPacoteDeCenario(PacoteDeRetratoDeCenario(estrago.Interior).CopyData()).Celulas.Count == 0,
						  $"mundo {(renascido == null ? "nao nasceu" : "nasceu")}; {_cenarioCaido.Count} zona(s) na lista");
				AfirmarPd("**A OBRA DA SALA DO TEMPO ATRAVESSA AS DUAS PARADAS**: ela e dado de disco, nao estrago de sessao -- continua na lista, na Sala DELA, barra na copia "
						  + "que renasce, e nao barra na Sala da Terra",
						  maquinaDaSala != null && chaoDaSala is { } ondeEstava && _noChao.Contains(maquinaDaSala)
						  && MapaDaZonaOuCatalogo(salaDoMundo) is { } copiaNova && copiaNova.BlockedCell(ondeEstava.X, ondeEstava.Y)
						  && MapaDaZonaOuCatalogo(ZoneKey.Premade(ZonaDaSala)) is { } daTerra && !daTerra.BlockedCell(ondeEstava.X, ondeEstava.Y));
				if (maquinaDaSala != null) IntrTirarObra(maquinaDaSala);
				Stop();   // a parte 8 comeca de um `Stop` sozinho de producao
			}
			else Stop();

			GD.Print("[parada] -- 8) a ultima subida, depois do `Stop` sozinho de producao --");
			AfirmarPd("(montagem) a porta sobe de novo", Sobe() && Running);
			(int vivos9, ulong quem9) = Povoa();
			AfirmarPd("depois do `Stop` sozinho o mundo tambem nasce povoado, com os mesmos habitantes do boot", vivos9 == vivosDoZero && quem9 == quemDoZero,
					  $"{vivos9} habitante(s) agora, {vivosDoZero} no boot");
			bool aDeNovo = mesa.Entra(a) && mesa.Loga(a, contaA);
			AfirmarPd("...a mesma conta loga de novo e e aceita", aDeNovo && a.Slots != null && a.Recusa == null, a.Recusa ?? "");
			corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			AfirmarPd("...e C entra, sozinho, num servidor que ninguem hospedou: jogador comum, fora da sala (o `Stop` sozinho nao o gravou la), com a sala vazia e sem torneio",
					  corpoC != null && !EhAdmin(corpoC) && _provaDoAnfitriao == null && QuantosNaSala() == 0 && !TorneioAtivo
					  && corpoC != null && !string.Equals(corpoC.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase), $"C em {corpoC?.Zone.Name ?? "nada"}; {QuantosNaSala()} na sala");
			AfirmarPd("a bancada fez quinze subidas e rodou uma vez so (a reentrada mandou as outras catorze embora)",
					  subidas == 15 && _paradaChamadas == 15, $"{subidas} subida(s), {_paradaChamadas} chamada(s)");

			// O ULTIMO, DE PROPOSITO: este defeito poe um golpe de verdade em cima de um dos dois corpos da bancada.
			GD.Print("[parada] -- 9) com o DEFEITO INJETADO, na ultima parada: a saida do embate de antes (grava antes de soltar, e solta com a cena de fecho) --");
			corpoB = mesa.EntraNoMundo(b, contaB, nomeB);
			palco = Posiciona(corpoB, corpoC);
			emEmbate = Embate(corpoB, corpoC);
			Defeito(saidaDeAntes: true);
			SalvarEParar();
			CharacterSave? castigoB = mesa.NoDisco(contaB), castigoC = mesa.NoDisco(contaC);
			AfirmarPd("(defeito injetado: a saida de antes) **O PRIMEIRO A SER DERRUBADO VAI PRO DISCO OCULTO**, e a cena de fecho acontece em cima de quem a parada derrubou -- a regua sabe reprovar",
					  palco && emEmbate && castigoB != null && castigoC != null && (castigoB.Ficha.isconcealed || castigoC.Ficha.isconcealed),
					  castigoB == null || castigoC == null ? "sem save" : $"oculto no disco: B {castigoB.Ficha.isconcealed}, C {castigoC.Ficha.isconcealed}");
		}
		catch (Exception e)
		{
			AfirmarPd("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			ParadaNaoDerrubaDeTeste = ParadaCobraAFusaoDeTeste = SaidaDoEmbateDeAntesDeTeste = ParadaLembraDaSessaoDeTeste = false;
			ParadaLembraDoEstragoDeTeste = false;
			EscutaDeCenario = null;
			(_torneioCfg.WoSegundos, _torneioCfg.ContagemSegundos, _torneioCfg.IntervaloSegundos) = relogiosDoTorneio;
			mesa.Fecha();
			_paradaRodando = false;
			GD.Print($"[parada] ================ {_pdOk} OK, {_pdFalhou} FALHA(S) ================");
		}
	}
}
