using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA LENHA (`--lenhateste`) -- o pedido do dono de 2026-10-10: *"derrubar arvores no soco (durante
/// combate n conta) vc ganha uma quantidade pequena de resources [...] essas arvores dps de um tempo voltam ou
/// spawnam em outro local."*
///
/// No PRIMEIRO LOGIN, e hospedando: "quem paga e JOGADOR" pede um corpo com `Peer` -- o do host. O segundo jogador
/// e um corpo forjado com o `Peer` do host emprestado; o NPC e um forjado sem `Peer`. Tudo passa pelas funcoes de
/// producao: o soco e o `SocarCenario` (e uma vez o `Atacar` inteiro, o da tecla), o tiro de ki e o `Disparar`, a
/// queda sem soco e o `RacharChao`/`DerrubarCenario`, a gravidade que esmaga e o `TickDoEsmagamento`, a volta e o
/// `TickDaLenha`, o retrato e o `MandarCenario`, o que ocupa o lugar e o `ComandoDeBloco` do modo de construir, e
/// quem refaz o cenario e o verb do admin.
///
/// O PALCO E A TERRA DE VERDADE (e, na ultima secao, um mundo sorteado da carta). O que a bancada derruba volta
/// no `finally`, com o relogio e a ficha do host.
///
/// Os defeitos injetados -- cada um lido pela propria linha da regra:
///   `ArvoresDoMapa.SoAPastaDeTeste`        as arvores de folha de turf (a floresta de Icer) voltam a ser parede
///   `Lenha.TroncoComoParedeDeTeste`        a arvore volta a ser parede pro soco (o novato nunca derruba)
///   `Lenha.PagaEmCombateDeTeste`           a arvore derrubada no meio da briga paga
///   `Lenha.EsmagadoContaComoBriga = false` (a constante do dono, invertida) o esmagado pela gravidade recebe
///   `ApanharDoMundoEBrigaDeTeste`          a briga lida da tag inteira: quem acabou de se aclimatar fica 90 s sem receber
///   `TocoMudoDeTeste`                      a arvore cai e ninguem fica sabendo que ficou um toco
///   `TocoFicaSobAObraDeTeste`              o toco continua desenhado debaixo do que construiram em cima
///   `TocoNaoVoltaDeTeste`                  a nave decola de cima do toco e ele nao volta: terra batida ate a arvore brotar
///   `ArteDoToco.CascaPadraoDeTeste`        o toco de madeira no pe da arvore de gelo
///   `ZoneCollision.CelulaAbertaIgnoraObraDeTeste`   a maquina assentada em cima do toco nao barra o corpo
///   `Lenha.PagaQualquerUmDeTeste`          NPC e corpo possuido recebem
///   `RebrotaNaHoraDeTeste`                 da pra derrubar a mesma arvore em laco
///   `ArvoreCaidaNaoRebrotaDeTeste`         a arvore que cai nunca mais volta
///   `RebrotaSemOlharOLugarDeTeste`         ela nasce por cima da parede de alguem
///   `RebrotaForaDoFunilDeTeste`            a rebrota so cai a soco
///   `RetratoDeLenhaVazioDeTeste`           quem chega nao ve as que voltaram
///   `MundoRenasceSemOEstragoDeTeste`       o mundo sorteado renasce com o toco fechado
///
///     Godot --headless --path . --host --rede 8150 --lenhateste --raca Human --conta bancada_lenha --nome Lenhador
/// </summary>
public sealed partial class GameServer
{
	private bool _lenhaDeTeste;
	private int _lhOk, _lhFalhou;

	private void AfirmarLenha(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _lhOk++; GD.Print($"[lenha]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _lhFalhou++; GD.PrintErr($"[lenha]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>
	/// Le um pacote `S2C.Lenha` como o cliente le: opcode, modo, e o corpo de cada modo. `Arvores` e a lista do modo
	/// (as rebrotas do retrato; a arvore do `Brotou`, do `Caiu`, do `Toco` e do `SemToco`); `Tocos` so o retrato tem.
	/// </summary>
	private static (byte Modo, ulong Zona, List<(int X, int Y, TileDeArvore Tile)> Arvores, bool Poeira,
					List<(int X, int Y, TileDeArvore Tile)> Tocos) LerPacoteDeLenha(byte[] fio)
	{
		var r = new NetDataReader(fio);
		r.GetByte();   // o opcode
		byte modo = r.GetByte();
		ulong zona = 0;
		bool poeira = false;
		var arvores = new List<(int X, int Y, TileDeArvore Tile)>();
		var tocos = new List<(int X, int Y, TileDeArvore Tile)>();

		(int, int, TileDeArvore) Uma() => (r.GetUShort(), r.GetUShort(), new TileDeArvore(r.GetUShort(), r.GetUShort(), r.GetUShort()));

		if (modo == Protocol.LenhaRetrato)
		{
			zona = r.GetULong();
			for (int n = r.GetInt(); n > 0; n--) arvores.Add(Uma());
			for (int n = r.GetInt(); n > 0; n--) tocos.Add(Uma());
		}
		else if (modo is Protocol.LenhaBrotou or Protocol.LenhaToco) arvores.Add(Uma());
		else if (modo == Protocol.LenhaCaiu)
		{
			arvores.Add((r.GetUShort(), r.GetUShort(), default));
			poeira = r.GetBool();
		}
		else if (modo == Protocol.LenhaSemToco) arvores.Add((r.GetUShort(), r.GetUShort(), default));
		return (modo, zona, arvores, poeira, tocos);
	}

	/// <summary>As arvores dos pacotes de UM modo, entre os que foram pra ZONA enquanto a escuta estava ligada.</summary>
	private static List<(int X, int Y, TileDeArvore Tile)> DaZona(List<(int Para, byte[] Fio)> fio, byte modo) =>
		[.. fio.Where(f => f.Para == 0).Select(f => LerPacoteDeLenha(f.Fio)).Where(p => p.Modo == modo).Select(p => p.Arvores[0])];

	/// <summary>
	/// UMA ARVORE DE MENTIRA, pra regua do toco (`ArteDoToco`): copa redonda em cima, tronco de 6 pixels, raiz que
	/// abre no pe. Com <paramref name="copaAteOChao"/> ela vira um pinheiro: a copa desce alargando ate a base, e nao
	/// ha tronco a vista.
	/// </summary>
	private static byte[] ArvoreDeMentira(int w, int h, bool copaAteOChao, (byte R, byte G, byte B) casca)
	{
		var px = new byte[w * h * 4];
		void Por(int x, int y, byte r, byte g, byte b)
		{
			if (x < 0 || y < 0 || x >= w || y >= h) return;
			int i = (y * w + x) * 4;
			px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255;
		}

		int meio = w / 2;
		if (copaAteOChao)
		{
			// um triangulo de folha, da ponta ate a base
			for (int y = 0; y < h; y++)
				for (int x = meio - 1 - y * (meio - 2) / (h - 1); x <= meio + y * (meio - 2) / (h - 1); x++) Por(x, y, 30, 120, 60);
			return px;
		}

		int copa = h * 6 / 10;                              // a copa ocupa os 60% de cima
		for (int y = 0; y < copa; y++)
			for (int x = 2; x < w - 2; x++) Por(x, y, 40, 140, 50);
		for (int y = copa; y < h - 3; y++)                  // o tronco
			for (int x = meio - 3; x < meio + 3; x++) Por(x, y, casca.R, casca.G, casca.B);
		for (int y = h - 3; y < h; y++)                     // a raiz abre tres pixels pra cada lado
			for (int x = meio - 6; x < meio + 6; x++) Por(x, y, casca.R, casca.G, casca.B);
		return px;
	}

	private void RodarBancadaDaLenha(ServerPlayer pl)
	{
		_lhOk = _lhFalhou = 0;
		GD.Print("\n[lenha] ================ A LENHA: A ARVORE QUE CAI A SOCO, PAGA E REBROTA (dono, 2026-10-10) ================");

		const int T = ZoneCollision.TileSize;
		ZoneKey terra = pl.Zone;
		ZoneCollision? mapaDaTerra = MapaDaZonaOuCatalogo(terra);

		// ---- o que a bancada mexe e devolve ----
		Vec2 posDeAntes = pl.Pos;
		Facing olharDeAntes = pl.Facing;
		double recursosDeAntes = pl.Ficha.Recursos, zeniDeAntes = pl.Ficha.Zeni, bpDeAntes = pl.Ficha.expressedBP;
		double tagDeAntes = pl.Combate.EmCombate, lutaDeAntes = pl.Combate.LutandoDeVerdade, recargaDeAntes = pl.Combate.Recarga;
		double brigaDeAntes = pl.Combate.BrigandoComAlguem;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		List<BlocoDePe> blocosDeAntes = [.. _bases.Values.SelectMany(z => z.Celulas.Values)];

		// O HOST AINDA NAO ESTA NA LISTA DA ZONA (as bancadas de 1o login rodam antes dessa linha do `Entrar`), e a
		// lenha pergunta a ela quem pisa onde e a quem avisar.
		List<ServerPlayer> daZona = ZoneList(terra.Hash);
		bool entrouPelaBancada = !daZona.Contains(pl);
		if (entrouPelaBancada) daZona.Add(pl);

		var usadas = new HashSet<(int X, int Y)>();
		var semToco = new List<(int X, int Y)>();   // derrubadas com o defeito "nao vira toco": voltam a mao
		var obrasDaBancada = new List<Obra>();
		ServerPlayer? vizinho = null;
		ZoneKey? mundo = null;

		try
		{
			AfirmarLenha("(montagem) o host nasceu na Terra, e ela tem mapa de colisao",
						 terra.Equals(ZonaDaBancadaDeProjetil) && mapaDaTerra != null, terra.ToString());
			if (!terra.Equals(ZonaDaBancadaDeProjetil) || mapaDaTerra is not { } mapa) return;

			// ============================================================ ajudantes
			Vec2 AoSul(int cx, int cy) => new(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
			Vec2 Em(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
			Vec2 Meio(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f);

			// A proxima arvore do mapa ainda de pe, a mais perto do berco, socavel do sul.
			(int X, int Y, TileDeArvore Tile) Arvore()
			{
				(int X, int Y, TileDeArvore Tile) a = ArvoreSocavelPertoDeTeste(terra, posDeAntes, usadas)
					?? throw new InvalidOperationException("acabaram as arvores socaveis da Terra");
				usadas.Add((a.X, a.Y));
				return a;
			}

			// Poe o corpo um tile ao sul, virado pro norte, com esta forca, e soca ate a celula deixar de barrar.
			int Socar(ServerPlayer quem, ZoneCollision m, int cx, int cy, double bp, int max = 12)
			{
				quem.Pos = AoSul(cx, cy);
				quem.Facing = Facing.North;
				quem.Ficha.expressedBP = bp;
				int n = 0;
				while (n < max && m.BlockedCell(cx, cy)) { SocarCenario(quem); n++; }
				return n;
			}

			// O relogio de parede anda, e a passada de producao roda (o `TickDaLenha`, com a espera de 1 s dele).
			void PassarOTempo(double segundos)
			{
				AdiantoDoRelogioDeTeste += (long)(segundos * 1000);
				// QUATRO PASSADAS: cada uma devolve ate `RebrotasPorPassada` arvores por zona.
				for (int i = 0; i < 4; i++) { AdiantoDoRelogioDeTeste += MsEntrePassadasDaLenha + 100; TickDaLenha(); }
			}
			double depoisDoPrazo = Lenha.RebrotaMaxS + 5;

			bool NaFaixa(double recursos) => recursos >= Lenha.RecursosMinimos && recursos <= Lenha.RecursosMaximos;

			pl.Combate.EmCombate = 0;
			pl.Combate.LutandoDeVerdade = 0;

			// ============================================================ 1) AS CONTAS
			GD.Print("[lenha] -- 1) as contas: quantos socos, quanto rende, quando paga --");
			AfirmarLenha("o mais fraco dos corpos derruba em 5 socos; BP 5 em 4; BP 7 em 3; BP 10 em 2; e quem expressa 20 (o que ja abria parede) em UM",
						 Lenha.SocosPraDerrubar(0.5) == 5 && Lenha.SocosPraDerrubar(4) == 5 && Lenha.SocosPraDerrubar(5) == 4
						 && Lenha.SocosPraDerrubar(7) == 3 && Lenha.SocosPraDerrubar(10) == 2 && Lenha.SocosPraDerrubar(19.9) == 2
						 && Lenha.SocosPraDerrubar(20) == 1 && Lenha.SocosPraDerrubar(1e9) == 1,
						 $"{Lenha.SocosPraDerrubar(0.5)}/{Lenha.SocosPraDerrubar(5)}/{Lenha.SocosPraDerrubar(7)}/{Lenha.SocosPraDerrubar(10)}/{Lenha.SocosPraDerrubar(20)}");

			bool semprNaFaixa = true;
			for (int i = 0; i <= 1000; i++) semprNaFaixa &= NaFaixa(Lenha.RecursosDaQueda(i / 1000.0));
			AfirmarLenha($"a queda rende de {Lenha.RecursosMinimos} a {Lenha.RecursosMaximos} recursos, as duas pontas incluidas, e nunca fora disso",
						 Lenha.RecursosDaQueda(0) == Lenha.RecursosMinimos && Lenha.RecursosDaQueda(0.9999) == Lenha.RecursosMaximos
						 && Lenha.RecursosDaQueda(1) == Lenha.RecursosMaximos && semprNaFaixa);
			AfirmarLenha("paga: jogador com as proprias maos e fora de combate SIM; em combate NAO; quem nao e jogador NAO",
						 Lenha.Paga(true, false) && !Lenha.Paga(true, true) && !Lenha.Paga(false, false) && !Lenha.Paga(false, true));
			AfirmarLenha($"o prazo da rebrota e sorteado entre {Lenha.RebrotaMinS:0} e {Lenha.RebrotaMaxS:0} segundos",
						 Lenha.SegundosAteRebrotar(0) == Lenha.RebrotaMinS && Lenha.SegundosAteRebrotar(1) == Lenha.RebrotaMaxS
						 && Lenha.SegundosAteRebrotar(0.5) > Lenha.RebrotaMinS && Lenha.SegundosAteRebrotar(0.5) < Lenha.RebrotaMaxS);

			// O TOCO SAI DA ARTE DA PROPRIA ARVORE (`ArteDoToco`). Tres arvores de mentira de 32x40: uma de casca
			// marrom, a mesma de casca de gelo, e um pinheiro (a copa ate o chao, sem tronco a vista).
			const int LA = 32, HA = 40;
			(byte R, byte G, byte B) marrom = (120, 80, 40), gelo = (150, 200, 240);
			TocoDerivado? deMadeira = ArteDoToco.Derivar(ArvoreDeMentira(LA, HA, copaAteOChao: false, marrom), LA, HA);
			TocoDerivado? deGelo = ArteDoToco.Derivar(ArvoreDeMentira(LA, HA, copaAteOChao: false, gelo), LA, HA);
			TocoDerivado? dePinheiro = ArteDoToco.Derivar(ArvoreDeMentira(LA, HA, copaAteOChao: true, marrom), LA, HA);

			// A cor de um pixel do toco, em coordenada do recorte da ARVORE; e o miolo do topo do corte.
			(int R, int G, int B, int A) Pixel(TocoDerivado t, int x, int y)
			{
				int tx = x - t.X, ty = y - t.Y;
				if (tx < 0 || ty < 0 || tx >= t.Largura || ty >= t.Altura) return (0, 0, 0, 0);
				int i = (ty * t.Largura + tx) * 4;
				return (t.Rgba[i], t.Rgba[i + 1], t.Rgba[i + 2], t.Rgba[i + 3]);
			}
			(int R, int G, int B, int A) Miolo(TocoDerivado t) => Pixel(t, LA / 2, HA - 1 - t.AlturaDoCorte);

			AfirmarLenha("O TOCO SAI DA ARTE DA ARVORE: fica a RAIZ dela inteira (a mesma largura, encostada na base, pixel por pixel) e um pedaco do tronco, cortado bem abaixo da copa",
						 deMadeira is { Sintetico: false, LarguraDoTopo: 6, Largura: 12 } tocoM && tocoM.X == LA / 2 - 6 && tocoM.Y + tocoM.Altura == HA
						 && tocoM.AlturaDoCorte is >= 3 and <= 8 && tocoM.Altura <= 12
						 && Pixel(tocoM, LA / 2 - 6, HA - 1) == (marrom.R, marrom.G, marrom.B, 255) && Pixel(tocoM, LA / 2, 10).A == 0,
						 deMadeira == null ? "nulo" : $"{deMadeira.Largura}x{deMadeira.Altura} em ({deMadeira.X},{deMadeira.Y}), corte a {deMadeira.AlturaDoCorte} linhas da base, topo de {deMadeira.LarguraDoTopo} px");
			(int R, int G, int B, int A) miMadeira = deMadeira == null ? default : Miolo(deMadeira), miGelo = deGelo == null ? default : Miolo(deGelo);
			AfirmarLenha("...com o TOPO DO CORTE por cima, da cor da casca clareada: madeira clara na arvore marrom, gelo claro na arvore de gelo -- o toco bate com a arvore que caiu",
						 miMadeira.A == 255 && miMadeira.R > marrom.R + 40 && miMadeira.R > miMadeira.B + 20 && miGelo.A == 255 && miGelo.B > miGelo.R && miGelo.B > 200,
						 $"miolo da marrom {miMadeira}, miolo da de gelo {miGelo}");

			(int R, int G, int B, int A) miPinheiro = dePinheiro == null ? default : Miolo(dePinheiro);
			AfirmarLenha("ARVORE SEM TRONCO A VISTA (o pinheiro: a copa vai ate o chao): o toco e sintetico, de casca padrao, no pe dela -- e nao uma fatia da copa",
						 dePinheiro is { Sintetico: true } tocoP && tocoP.Y + tocoP.Altura == HA && tocoP.Largura <= 14 && tocoP.Altura <= 14
						 && miPinheiro.A == 255 && miPinheiro.R > miPinheiro.G && miPinheiro.G > miPinheiro.B,
						 dePinheiro == null ? "nulo" : $"{dePinheiro.Largura}x{dePinheiro.Altura}, miolo {miPinheiro}");

			ArteDoToco.CascaPadraoDeTeste = true;
			try
			{
				TocoDerivado? geloComDefeito = ArteDoToco.Derivar(ArvoreDeMentira(LA, HA, copaAteOChao: false, gelo), LA, HA);
				(int R, int G, int B, int A) miDefeito = geloComDefeito == null ? default : Miolo(geloComDefeito);
				AfirmarLenha("(defeito injetado: o topo sai sempre da casca padrao) a arvore de GELO ganha um toco com topo de MADEIRA -- o \"toco de carvalho na arvore de neve\" que o pedido proibe",
							 miDefeito.A == 255 && miDefeito.R > miDefeito.B + 20, $"miolo {miDefeito}");
			}
			finally { ArteDoToco.CascaPadraoDeTeste = false; }

			// ============================================================ 2) ONDE HA ARVORE
			GD.Print("[lenha] -- 2) onde ha arvore --");
			ArvoresDoMapa? arvores = ArvoresDa(terra);
			AfirmarLenha("o servidor SABE onde ha arvore na Terra: centenas delas, lidas do `.pedacos` do mapa",
						 arvores is { Total: > 100 }, $"{arvores?.Total ?? 0} arvores");
			if (arvores == null) return;
			AfirmarLenha("...e toda arvore e uma celula que o arquivo de colisao marca (ela para o corpo)",
						 arvores.Todas().All(c => mapa.BloqueadaNoArquivo(c.X, c.Y)));

			Obra? macieira = _noChao.FirstOrDefault(o => o.DoMapa && o.Tipo == Jandirus.Core.Items.CatalogoDeItens.ArvoreDeMaca && o.Zona.Equals(terra));
			if (macieira != null)
			{
				(int mx, int my) = Jandirus.Core.Tech.CatalogoDeObras.Celula(macieira.X, macieira.Y);
				AfirmarLenha("a MACIEIRA nao e lenha: ela e uma construcao (da fruta, pode ter dono), e a celula dela nao esta entre as arvores",
							 !arvores.Tem(mx, my, out _) && !ArvoreDePe(terra, mx, my), $"({mx},{my})");
			}
			else AfirmarLenha("(montagem) a Terra tem uma macieira do mapa de pe", false);

			AfirmarLenha("FORA DE PLANETA nao ha lenha: o Mirante tem arvore no mapa, e o servidor nao a trata como lenha",
						 !Espaco.EhPlaneta(ZoneKey.Premade("Lookout")) && ArvoresDa(ZoneKey.Premade("Lookout")) == null);

			// AS DE FOLHA DE TURF: a branca de Icer, a azul e a verde de Namek. Sao UM quadro de uma folha que nao e de
			// arvore -- a pasta nao as acha, o nome acha (`ArvoresDoMapa.ArvoresEmFolhaDeTurf`).
			ZoneKey icer = ZoneKey.Premade("Icer"), namek = ZoneKey.Premade("Namek");
			CatalogoDeTiles indice = TilesDaLenha();
			TileDeArvore Quadro(string atlas, string estado) =>
				indice.Achar(atlas, estado) is { } q ? new TileDeArvore((ushort)q.Fonte, (ushort)q.X, (ushort)q.Y) : TileDeArvore.SemDesenho;
			int Quantas(ZoneKey z, TileDeArvore t) =>
				ArvoresDa(z) is { } la ? la.Todas().Count(c => la.Tem(c.X, c.Y, out TileDeArvore dela) && dela == t) : 0;

			TileDeArvore branca = Quadro("Turfs 1", "frozentree"), azul = Quadro("Turfs 1", "smalltree"), verde = Quadro("Turfs 2", "treeb");
			int brancasDeIcer = Quantas(icer, branca), brancasDaTerra = Quantas(terra, branca);
			int azuisDeNamek = Quantas(namek, azul), verdesDeNamek = Quantas(namek, verde);
			AfirmarLenha("as arvores que o DM desenhou em folha de TURF tambem sao lenha: a floresta congelada de Icer (a branca), a azul e a verde de Namek, e as brancas da neve da Terra",
						 branca.TemDesenho && azul.TemDesenho && verde.TemDesenho
						 && brancasDeIcer > 100 && azuisDeNamek > 10 && verdesDeNamek > 10 && brancasDaTerra > 5,
						 $"Icer {brancasDeIcer} brancas de {ArvoresDa(icer)?.Total}; Namek {azuisDeNamek} azuis e {verdesDeNamek} verdes de {ArvoresDa(namek)?.Total}; Terra {brancasDaTerra} brancas de {arvores.Total}");

			ArvoresDoMapa.SoAPastaDeTeste = true;
			try
			{
				ArvoresDoMapa? soPelaPasta = LerArvoresDoArquivo(icer);   // le de novo, sem tocar no que o servidor guarda
				AfirmarLenha("(defeito injetado: so a pasta diz o que e arvore) a floresta congelada de Icer deixa de ser lenha -- perto de metade das arvores do planeta volta a ser parede comum",
							 soPelaPasta != null && ArvoresDa(icer) is { } deIcer && soPelaPasta.Total == deIcer.Total - brancasDeIcer,
							 $"{soPelaPasta?.Total} em vez de {ArvoresDa(icer)?.Total}");
			}
			finally { ArvoresDoMapa.SoAPastaDeTeste = false; }

			// ============================================================ 3) O SOCO DERRUBA E PAGA
			GD.Print("[lenha] -- 3) o soco, fora de combate: derruba e paga --");
			(int X, int Y, TileDeArvore Tile) a1 = Arvore();
			pl.Ficha.Recursos = 0;
			SorteioDaLenhaDeTeste = 0;
			pl.Pos = AoSul(a1.X, a1.Y);
			pl.Facing = Facing.North;
			pl.Ficha.expressedBP = 5;
			AfirmarLenha("(montagem) a arvore escolhida esta de pe, barrando, e o host esta colado nela olhando pra ela",
						 ArvoreDePe(terra, a1.X, a1.Y) && mapa.BlockedCell(a1.X, a1.Y) && !mapa.BlockedCell(a1.X, a1.Y + 1), $"({a1.X},{a1.Y})");

			List<string> falas = Ouvir(() => { SocarCenario(pl); SocarCenario(pl); SocarCenario(pl); });
			AfirmarLenha("tres socos de um novato (BP 5): a arvore estala, continua de pe e ninguem recebe nada -- e ele sabe quantos faltam",
						 mapa.BlockedCell(a1.X, a1.Y) && ArvoreDePe(terra, a1.X, a1.Y) && pl.Ficha.Recursos == 0
						 && Disse(falas, "estala") && Disse(falas, "mais um soco"), Ultimos(falas));

			EscutaDeLenha = [];
			falas = Ouvir(() => SocarCenario(pl));
			List<(int Para, byte[] Fio)> daQueda = EscutaDeLenha;
			EscutaDeLenha = null;
			AfirmarLenha("**O QUARTO SOCO DERRUBA** (BP 5 leva um quarto do tronco por golpe): a celula abre, entra no estrago da zona e a arvore vira toco",
						 !mapa.BlockedCell(a1.X, a1.Y) && CenarioJaCaiu(terra, a1.X, a1.Y) && TocoDeTeste(terra, a1.X, a1.Y)
						 && !ArvoreDePe(terra, a1.X, a1.Y), Ultimos(falas));
			AfirmarLenha($"**E QUEM DERRUBOU RECEBE**: com o sorteio no zero, exatamente o minimo (+{Lenha.RecursosMinimos}) -- e fica sabendo",
						 pl.Ficha.Recursos == Lenha.RecursosMinimos && Disse(falas, $"+{Lenha.RecursosMinimos} recursos"),
						 $"saldo {pl.Ficha.Recursos:0.##} | {Ultimos(falas)}");
			List<(int X, int Y, TileDeArvore Tile)> tocosDaQueda = DaZona(daQueda, Protocol.LenhaToco);
			AfirmarLenha("**FICA UM TOCO NO LUGAR DELA** (dono: \"sim seria bom um sprite\"): quem esta na zona recebe um `LenhaToco` com a celula e o tile DA ARVORE que caiu -- e dele que o cliente tira o desenho. O toco nao barra o passo",
						 tocosDaQueda.Count == 1 && tocosDaQueda[0] == (a1.X, a1.Y, a1.Tile) && TocoAVistaDeTeste(terra, a1.X, a1.Y)
						 && !mapa.BlockedCell(a1.X, a1.Y), $"{tocosDaQueda.Count} pacote(s) de toco");

			(int X, int Y, TileDeArvore Tile) a2 = Arvore();
			double antes = pl.Ficha.Recursos;
			SorteioDaLenhaDeTeste = 0.9999;
			int socos = Socar(pl, mapa, a2.X, a2.Y, bp: 5000);
			AfirmarLenha($"quem expressa 20 ou mais derruba de UM soco, e com o sorteio no teto recebe o maximo (+{Lenha.RecursosMaximos})",
						 socos == 1 && !mapa.BlockedCell(a2.X, a2.Y) && pl.Ficha.Recursos == antes + Lenha.RecursosMaximos,
						 $"{socos} soco(s), saldo {antes:0.##} -> {pl.Ficha.Recursos:0.##}");

			(int X, int Y, TileDeArvore Tile) mudo = Arvore();
			TocoMudoDeTeste = true;
			try
			{
				EscutaDeLenha = [];
				Socar(pl, mapa, mudo.X, mudo.Y, bp: 5000);
				AfirmarLenha("(defeito injetado: o toco e mudo) a arvore cai, vira toco pro servidor, e ninguem fica sabendo: nenhum pacote, nada a vista -- a terra batida de antes do pedido",
							 !mapa.BlockedCell(mudo.X, mudo.Y) && TocoDeTeste(terra, mudo.X, mudo.Y) && !TocoAVistaDeTeste(terra, mudo.X, mudo.Y)
							 && DaZona(EscutaDeLenha, Protocol.LenhaToco).Count == 0);
			}
			finally { TocoMudoDeTeste = false; EscutaDeLenha = null; }

			(int X, int Y, TileDeArvore Tile) a3 = Arvore();
			antes = pl.Ficha.Recursos;
			SorteioDaLenhaDeTeste = null;   // o sorteio de verdade, do `_rng` do servidor
			socos = Socar(pl, mapa, a3.X, a3.Y, bp: 0.3);
			AfirmarLenha("o corpo MAIS FRACO que existe (BP 0,3) derruba em cinco socos, e o sorteio do servidor paga dentro da faixa",
						 socos == 5 && !mapa.BlockedCell(a3.X, a3.Y) && NaFaixa(pl.Ficha.Recursos - antes),
						 $"{socos} socos, +{pl.Ficha.Recursos - antes:0.##}");

			// UMA DE FOLHA DE TURF: a branca da neve da Terra. A mesma regra -- e ela volta com o tile DELA (secao 8).
			(int X, int Y, TileDeArvore Tile)? daNeve = ArvoreSocavelPertoDeTeste(terra, posDeAntes, usadas, soDesteTile: branca);
			AfirmarLenha("(montagem) ha uma arvore branca socavel na Terra", daNeve != null);
			if (daNeve is not { } neve) return;
			usadas.Add((neve.X, neve.Y));
			antes = pl.Ficha.Recursos;
			socos = Socar(pl, mapa, neve.X, neve.Y, bp: 3);
			AfirmarLenha("a arvore de folha de TURF (a branca) cai e paga como as outras: cinco socos de um novato de BP 3 -- antes ela era parede, e ele nunca a derrubava",
						 socos == 5 && !mapa.BlockedCell(neve.X, neve.Y) && TocoDeTeste(terra, neve.X, neve.Y) && NaFaixa(pl.Ficha.Recursos - antes),
						 $"({neve.X},{neve.Y}): {socos} socos, +{pl.Ficha.Recursos - antes:0.##}");

			// PELA TECLA DE VERDADE: o `Atacar` inteiro. Procura uma arvore sem ninguem na mira (a Terra tem gente).
			bool pelaTecla = false, semCombate = false;
			for (int tentativa = 0; tentativa < 4 && !pelaTecla; tentativa++)
			{
				(int X, int Y, TileDeArvore Tile) at = Arvore();
				pl.Pos = AoSul(at.X, at.Y);
				pl.Facing = Facing.North;
				pl.Ficha.expressedBP = 5000;
				pl.Combate.Recarga = 0;
				if (AlvoNaFrente(pl) != null) continue;
				antes = pl.Ficha.Recursos;
				Atacar(pl, Protocol.Golpe.Leve);
				pelaTecla = !mapa.BlockedCell(at.X, at.Y) && NaFaixa(pl.Ficha.Recursos - antes);
				semCombate = pl.Combate.EmCombate == 0;
			}
			AfirmarLenha("PELA TECLA DE VERDADE (`Atacar`, o golpe leve sem ninguem na frente): a arvore cai e paga", pelaTecla);
			AfirmarLenha("...e socar arvore NAO poe ninguem em combate: a tag continua zerada, e a proxima arvore tambem paga", semCombate);

			(int X, int Y, TileDeArvore Tile) a4 = Arvore();
			antes = pl.Ficha.Recursos;
			Lenha.TroncoComoParedeDeTeste = true;
			try
			{
				socos = Socar(pl, mapa, a4.X, a4.Y, bp: 5, max: 80);
				AfirmarLenha("(defeito injetado: a arvore volta a ser parede pro soco) 80 socos de um novato de BP 5 e ela continua de pe, sem render nada -- o mundo de antes: quem nao alcanca 20 nunca derruba",
							 socos == 80 && mapa.BlockedCell(a4.X, a4.Y) && pl.Ficha.Recursos == antes, $"{socos} socos");
			}
			finally { Lenha.TroncoComoParedeDeTeste = false; }

			// ============================================================ 4) EM COMBATE NAO CONTA
			GD.Print("[lenha] -- 4) durante combate nao conta --");
			(int X, int Y, TileDeArvore Tile) b1 = Arvore();
			antes = pl.Ficha.Recursos;
			pl.Combate.EntrarEmCombate();
			falas = Ouvir(() => socos = Socar(pl, mapa, b1.X, b1.Y, bp: 5000));
			AfirmarLenha("**EM COMBATE NAO CONTA**: com a tag de combate no ar a arvore cai (e vira toco), e NAO paga -- e o jogador ouve por que",
						 socos == 1 && !mapa.BlockedCell(b1.X, b1.Y) && TocoDeTeste(terra, b1.X, b1.Y) && pl.Ficha.Recursos == antes
						 && Disse(falas, "não rende recursos"), $"tag {pl.Combate.EmCombate:0.#} s | {Ultimos(falas)}");

			(int X, int Y, TileDeArvore Tile) b2 = Arvore();
			pl.Combate.EmCombate = 0.5;
			Socar(pl, mapa, b2.X, b2.Y, bp: 5000);
			AfirmarLenha("...ate o ULTIMO meio segundo da tag (a que acende a musica de luta: 90 s desde o ultimo golpe trocado)",
						 !mapa.BlockedCell(b2.X, b2.Y) && pl.Ficha.Recursos == antes);

			(int X, int Y, TileDeArvore Tile) b3 = Arvore();
			pl.Combate.EmCombate = 0;
			Socar(pl, mapa, b3.X, b3.Y, bp: 5000);
			AfirmarLenha("a tag venceu: a proxima arvore volta a pagar", NaFaixa(pl.Ficha.Recursos - antes), $"+{pl.Ficha.Recursos - antes:0.##}");

			(int X, int Y, TileDeArvore Tile) b4 = Arvore();
			antes = pl.Ficha.Recursos;
			pl.Combate.EntrarEmCombate();
			Lenha.PagaEmCombateDeTeste = true;
			try
			{
				Socar(pl, mapa, b4.X, b4.Y, bp: 5000);
				AfirmarLenha("(defeito injetado: a arvore que cai no meio da briga paga) o MESMO soco, com a MESMA tag no ar, rende recurso -- a regua sabe reprovar",
							 NaFaixa(pl.Ficha.Recursos - antes), $"+{pl.Ficha.Recursos - antes:0.##}");
			}
			finally { Lenha.PagaEmCombateDeTeste = false; }
			pl.Combate.EmCombate = 0;
			pl.Combate.LutandoDeVerdade = 0;
			pl.Combate.BrigandoComAlguem = 0;

			// ---- O ESMAGADO CONTA COMO EM COMBATE (dono, 2026-10-10: "sim conta como combate, n da pra mineirar se vc
			// nem consegue se mover") -- e o aclimatado recebe na hora ----
			// O esmagamento e o tique de producao (`TickDoEsmagamento`, 1 Hz): fere pelo funil de dano de sempre, e o
			// funil arma a tag de combate. Quem leva e um SEGUNDO JOGADOR forjado (corpo com dono na tela: o `Peer` do
			// host emprestado, devolvido a nulo no fim) -- o dano fica nele, que sai com a bancada.
			vizinho = Forjar("lh: o vizinho", AoSul(a2.X, a2.Y), 1000);
			vizinho.Peer = pl.Peer;
			vizinho.Conta = "bancada_lenha_vizinho";
			vizinho.Ficha.Recursos = 0;
			vizinho.Ficha.gravmult = 0;
			vizinho.Ficha.weight_ratio = 0;
			double dominaDeAntes = vizinho.Ficha.GravMastered;
			vizinho.Ficha.Planetgrav = 2 * Math.Max(dominaDeAntes, 1);   // o dobro do que ele domina
			CombatState doVizinho = vizinho.Combate;

			(int X, int Y, TileDeArvore Tile) peso1 = Arvore();
			TickDoEsmagamento();
			AfirmarLenha("(montagem) um jogador num chao 2x mais pesado que ele: o esmagamento o fere e a TAG de combate acende -- sem ele ter trocado um golpe com ninguem",
						 EhJogador(vizinho) && Esmagamento.Esmaga(vizinho.Ficha) && !Esmagamento.Prende(vizinho.Ficha)
						 && doVizinho.EmCombate > 0 && !doVizinho.EmBrigaComAlguem, $"tag {doVizinho.EmCombate:0.#} s, briga {doVizinho.BrigandoComAlguem:0.#} s");
			falas = Ouvir(() => socos = Socar(vizinho, mapa, peso1.X, peso1.Y, bp: 3));
			AfirmarLenha("**O ESMAGADO NAO RECEBE**: a arvore cai (cinco socos de BP 3) e vira toco, e ele nao ganha nada -- ouvindo o motivo CERTO: a gravidade, e nao uma briga que nao houve",
						 socos == 5 && !mapa.BlockedCell(peso1.X, peso1.Y) && TocoDeTeste(terra, peso1.X, peso1.Y) && vizinho.Ficha.Recursos == 0
						 && Disse(falas, "gravidade daqui te esmaga") && !Disse(falas, "briga") && !Disse(falas, "+"),
						 $"{socos} socos, +{vizinho.Ficha.Recursos:0.##} | {Ultimos(falas)}");

			// PELO PESO DE TREINO: a mesma regra (`Esmagamento.Razao` e a pior das duas), com a frase do peso.
			(int X, int Y, TileDeArvore Tile) peso2 = Arvore();
			vizinho.Ficha.Planetgrav = 1;
			vizinho.Ficha.weight_ratio = 2;
			falas = Ouvir(() => Socar(vizinho, mapa, peso2.X, peso2.Y, bp: 5000));
			AfirmarLenha("...e quem esta esmagado pelo PESO de treino (o dobro do limite) tambem nao recebe -- e a frase fala do peso que ele carrega",
						 Esmagamento.PorPeso(vizinho.Ficha) && !mapa.BlockedCell(peso2.X, peso2.Y) && vizinho.Ficha.Recursos == 0
						 && Disse(falas, "peso que você carrega") && !Disse(falas, "briga"), Ultimos(falas));
			vizinho.Ficha.weight_ratio = 0;
			vizinho.Ficha.Planetgrav = 2 * Math.Max(dominaDeAntes, 1);

			(int X, int Y, TileDeArvore Tile) peso3 = Arvore();
			Lenha.EsmagadoContaComoBriga = false;
			try
			{
				falas = Ouvir(() => Socar(vizinho, mapa, peso3.X, peso3.Y, bp: 5000));
				AfirmarLenha("(a constante `Lenha.EsmagadoContaComoBriga` invertida) o MESMO corpo esmagado, com a MESMA tag acesa, derruba e RECEBE -- e a linha que o dono troca pra tirar a arvore dessa regra",
							 Esmagamento.Esmaga(vizinho.Ficha) && doVizinho.EmCombate > 0 && !mapa.BlockedCell(peso3.X, peso3.Y)
							 && NaFaixa(vizinho.Ficha.Recursos), $"+{vizinho.Ficha.Recursos:0.##} | {Ultimos(falas)}");
			}
			finally { Lenha.EsmagadoContaComoBriga = true; }

			// ACLIMATOU: a maestria alcancou a gravidade do chao. O esmagamento parou; a tag que ele deixou continua acesa.
			(int X, int Y, TileDeArvore Tile) peso4 = Arvore();
			vizinho.Ficha.GravMastered = Esmagamento.Gravidade(vizinho.Ficha);
			antes = vizinho.Ficha.Recursos;
			falas = Ouvir(() => Socar(vizinho, mapa, peso4.X, peso4.Y, bp: 5000));
			AfirmarLenha("**O ACLIMATADO RECEBE, E NA HORA**: no mesmo chao de 2x, ja dominando 2x, a arvore paga -- com a tag de combate que o esmagamento deixou ainda acesa",
						 !Esmagamento.Esmaga(vizinho.Ficha) && doVizinho.EmCombate > 0 && !mapa.BlockedCell(peso4.X, peso4.Y)
						 && NaFaixa(vizinho.Ficha.Recursos - antes), $"tag {doVizinho.EmCombate:0.#} s, +{vizinho.Ficha.Recursos - antes:0.##} | {Ultimos(falas)}");

			(int X, int Y, TileDeArvore Tile) peso5 = Arvore();
			antes = vizinho.Ficha.Recursos;
			ApanharDoMundoEBrigaDeTeste = true;
			try
			{
				falas = Ouvir(() => Socar(vizinho, mapa, peso5.X, peso5.Y, bp: 5000));
				AfirmarLenha("(defeito injetado: a briga e lida da tag de combate inteira) o MESMO aclimatado, com a MESMA tag acesa, derruba e NAO recebe, ouvindo \"no meio de uma briga\" -- 90 s de castigo por ter se aclimatado",
							 !mapa.BlockedCell(peso5.X, peso5.Y) && vizinho.Ficha.Recursos == antes && Disse(falas, "briga"), Ultimos(falas));
			}
			finally { ApanharDoMundoEBrigaDeTeste = false; }

			// ESMAGADO E BRIGANDO: os dois motivos de uma vez. A frase e a da briga -- e ela que ele pode resolver primeiro.
			(int X, int Y, TileDeArvore Tile) peso6 = Arvore();
			vizinho.Ficha.GravMastered = dominaDeAntes;
			doVizinho.EntrarEmCombate();   // um golpe trocado com alguem, no mesmo chao pesado
			TickDoEsmagamento();
			antes = vizinho.Ficha.Recursos;
			falas = Ouvir(() => Socar(vizinho, mapa, peso6.X, peso6.Y, bp: 5000));
			AfirmarLenha("esmagado E brigando: nao recebe, e a frase e a da briga (o relogio `EmBrigaComAlguem` e o que separa os dois motivos)",
						 Esmagamento.Esmaga(vizinho.Ficha) && doVizinho.EmBrigaComAlguem && !mapa.BlockedCell(peso6.X, peso6.Y)
						 && vizinho.Ficha.Recursos == antes && Disse(falas, "no meio de uma briga") && !Disse(falas, "gravidade"), Ultimos(falas));

			vizinho.Ficha.Planetgrav = 1;
			doVizinho.EmCombate = 0;
			doVizinho.LutandoDeVerdade = 0;
			doVizinho.BrigandoComAlguem = 0;

			// ============================================================ 5) SEM SOCO NAO PAGA
			GD.Print("[lenha] -- 5) o tiro de ki nao derruba; e a arvore que cai sem soco (o chao que racha, o arremesso) vira toco e nao paga --");

			// O TIRO DE KI: uma bola de verdade (`Disparar`), do chao, colada na arvore e apontada pra ela.
			(int X, int Y, TileDeArvore Tile) c0 = Arvore();
			pl.Pos = AoSul(c0.X, c0.Y);
			pl.Facing = Facing.North;
			antes = pl.Ficha.Recursos;
			Projetil bola = Disparar(pl, BolaDaMira(), rumoDado: new Vec2(0, -1));
			VoarAteAcabar(bola);
			double doTronco = Math.Sqrt((bola.Pos - Meio(c0.X, c0.Y)).LengthSquared) / T;
			AfirmarLenha("o TIRO DE KI morre na arvore (pro tiro ela e parede): ela continua de pe, nao vira toco e ninguem recebe -- ki nao derruba lenha",
						 !Voo.AtravessaCenario(pl.Altitude) && !bola.Vivo && doTronco <= 1.5 && mapa.BlockedCell(c0.X, c0.Y)
						 && ArvoreDePe(terra, c0.X, c0.Y) && !TocoDeTeste(terra, c0.X, c0.Y) && pl.Ficha.Recursos == antes,
						 $"a bola acabou a {doTronco:0.0} tile(s) do tronco");

			(int X, int Y, TileDeArvore Tile) c1 = Arvore();
			pl.Pos = AoSul(c1.X, c1.Y);
			antes = pl.Ficha.Recursos;
			int rachadas = RacharChao(terra, Meio(c1.X, c1.Y), 1_000_000, raio: 0, chance: 1.0);
			AfirmarLenha("o CHAO QUE RACHA (golpe pesado, embate de ki, tecnica -- `RacharChao`) derruba a arvore pelo funil de sempre: ela cai e vira toco",
						 rachadas == 1 && !mapa.BlockedCell(c1.X, c1.Y) && TocoDeTeste(terra, c1.X, c1.Y), $"{rachadas} celula(s)");
			AfirmarLenha("...e NAO PAGA ninguem -- nem o jogador colado nela, fora de combate", pl.Ficha.Recursos == antes);

			(int X, int Y, TileDeArvore Tile) c2 = Arvore();
			pl.Pos = AoSul(c2.X, c2.Y);
			bool caiu = DerrubarCenario(terra, Meio(c2.X, c2.Y));
			AfirmarLenha("o ARREMESSO (`DerrubarCenario`, o corpo jogado contra ela) idem: cai, vira toco, ninguem recebe",
						 caiu && !mapa.BlockedCell(c2.X, c2.Y) && TocoDeTeste(terra, c2.X, c2.Y) && pl.Ficha.Recursos == antes);

			// ============================================================ 6) QUEM NAO E JOGADOR NAO RECEBE
			GD.Print("[lenha] -- 6) NPC e corpo possuido derrubam, e nao recebem --");
			ServerPlayer npc = Forjar("lh: um bicho", AoSul(a1.X, a1.Y), 1000);
			npc.Ficha.Recursos = 0;
			(int X, int Y, TileDeArvore Tile) d1 = Arvore();
			socos = Socar(npc, mapa, d1.X, d1.Y, bp: 5000);
			AfirmarLenha("um NPC derruba a arvore a soco como qualquer corpo -- e NAO recebe (nem ele, nem o jogador da zona)",
						 socos == 1 && !mapa.BlockedCell(d1.X, d1.Y) && TocoDeTeste(terra, d1.X, d1.Y)
						 && npc.Ficha.Recursos == 0 && pl.Ficha.Recursos == antes, $"NPC +{npc.Ficha.Recursos:0.##}, host +{pl.Ficha.Recursos - antes:0.##}");

			(int X, int Y, TileDeArvore Tile) d2 = Arvore();
			pl.Cerebro = new Jandirus.Core.Ai.Cerebro();   // a fera no comando do corpo do host
			try { Socar(pl, mapa, d2.X, d2.Y, bp: 5000); }
			finally { pl.Cerebro = null; }
			AfirmarLenha("o corpo do jogador POSSUIDO (a fera do Oozaru, a furia lendaria) derruba e nao recebe: quem socou nao foi o dono",
						 !mapa.BlockedCell(d2.X, d2.Y) && pl.Ficha.Recursos == antes);

			(int X, int Y, TileDeArvore Tile) d3 = Arvore();
			Lenha.PagaQualquerUmDeTeste = true;
			try
			{
				Socar(npc, mapa, d3.X, d3.Y, bp: 5000);
				AfirmarLenha("(defeito injetado: paga a quem derrubou, seja quem for) o NPC recebe -- a regua sabe reprovar",
							 NaFaixa(npc.Ficha.Recursos), $"NPC +{npc.Ficha.Recursos:0.##}");
			}
			finally { Lenha.PagaQualquerUmDeTeste = false; }

			// ============================================================ 7) UMA QUEDA, UM PAGAMENTO
			GD.Print("[lenha] -- 7) uma queda, um pagamento --");
			pl.Pos = AoSul(a1.X, a1.Y);
			pl.Facing = Facing.North;
			pl.Ficha.expressedBP = 5000;
			antes = pl.Ficha.Recursos;
			bool achouAlgo = false;
			for (int i = 0; i < 10; i++) achouAlgo |= SocarCenario(pl);
			AfirmarLenha("PAGA UMA VEZ SO: dez socos no lugar da arvore que ja caiu acertam o ar e nao rendem nada",
						 !achouAlgo && pl.Ficha.Recursos == antes && !mapa.BlockedCell(a1.X, a1.Y));

			// O SEGUNDO JOGADOR e o vizinho da secao 4, ja sem a gravidade em cima.
			(int X, int Y, TileDeArvore Tile) e1 = Arvore();
			pl.Ficha.Recursos = 0;
			vizinho.Ficha.Recursos = 0;
			pl.Pos = AoSul(e1.X, e1.Y);
			pl.Facing = Facing.North;
			pl.Ficha.expressedBP = 5;
			SocarCenario(pl); SocarCenario(pl); SocarCenario(pl);
			AfirmarLenha("(montagem) o segundo corpo e JOGADOR (tem dono na tela), e a arvore esta a um soco de cair",
						 EhJogador(vizinho) && mapa.BlockedCell(e1.X, e1.Y));
			Socar(vizinho, mapa, e1.X, e1.Y, bp: 5);
			AfirmarLenha("DOIS JOGADORES NA MESMA ARVORE: o tronco e dela, quem da o ULTIMO soco recebe e o outro nao -- uma queda, um pagamento",
						 !mapa.BlockedCell(e1.X, e1.Y) && NaFaixa(vizinho.Ficha.Recursos) && pl.Ficha.Recursos == 0,
						 $"vizinho +{vizinho.Ficha.Recursos:0.##}, host +{pl.Ficha.Recursos:0.##}");
			double pagoAoVizinho = vizinho.Ficha.Recursos;
			pl.Ficha.expressedBP = 5000;
			for (int i = 0; i < 5; i++) { SocarCenario(pl); SocarCenario(vizinho); }
			AfirmarLenha("...e os dois socando o lugar depois: nada", pl.Ficha.Recursos == 0 && vizinho.Ficha.Recursos == pagoAoVizinho);
			vizinho.Pos = AoSul(a2.X, a2.Y);

			// ============================================================ 8) A REBROTA
			GD.Print("[lenha] -- 8) a rebrota: depois do prazo ela volta, e paga de novo --");
			int tocosAgora = LenhaDeTeste(terra).Tocos;
			PassarALenha(NowMs());
			AfirmarLenha("logo depois da queda nada volta: os tocos esperam",
						 LenhaDeTeste(terra) == (tocosAgora, 0) && !mapa.BlockedCell(a1.X, a1.Y), $"{LenhaDeTeste(terra)}");

			// O PRAZO E CONTADO DE CADA QUEDA; as desta bancada cairam todas no mesmo instante do relogio.
			AdiantoDoRelogioDeTeste += (long)((Lenha.RebrotaMinS - 5) * 1000);
			TickDaLenha();
			AfirmarLenha($"cinco segundos antes do prazo MINIMO ({Lenha.RebrotaMinS:0} s) nenhuma arvore voltou", LenhaDeTeste(terra) == (tocosAgora, 0));

			List<(int X, int Y, TileDeArvore Tile)> aVistaAntes = TocosAVistaDeTeste(terra);
			EscutaDeLenha = [];
			PassarOTempo(Lenha.RebrotaMaxS - Lenha.RebrotaMinS + 10);
			List<(int Para, byte[] Fio)> noFio = EscutaDeLenha;
			EscutaDeLenha = null;
			((int X, int Y) Origem, TileDeArvore Tile)? volta = RebrotaDeTeste(terra, a1.X, a1.Y);
			AfirmarLenha("**PASSADO O PRAZO, A ARVORE VOLTA** -- no mesmo lugar: ha uma arvore de pe na celula, e ela barra de novo",
						 volta is { } v1 && v1.Origem == (a1.X, a1.Y) && mapa.BlockedCell(a1.X, a1.Y) && ArvoreDePe(terra, a1.X, a1.Y)
						 && !TocoDeTeste(terra, a1.X, a1.Y));
			AfirmarLenha("...com o MESMO tile da arvore que caiu (e o que o cliente desenha)",
						 volta is { } v2 && v2.Tile == a1.Tile && a1.Tile.TemDesenho, $"{volta?.Tile} / {a1.Tile}");
			AfirmarLenha("...e a branca da neve volta BRANCA: o tile de folha de turf, na celula dela",
						 RebrotaDeTeste(terra, neve.X, neve.Y) is { } vn && vn.Tile == branca && vn.Origem == (neve.X, neve.Y)
						 && mapa.BlockedCell(neve.X, neve.Y), $"{RebrotaDeTeste(terra, neve.X, neve.Y)?.Tile}");
			AfirmarLenha("...sem desfazer o estrago: a celula continua no retrato de cenario (a terra batida fica debaixo dela, e o cache do cliente nao precisa ser refeito)",
						 CenarioJaCaiu(terra, a1.X, a1.Y) && mapa.Rebrotada(a1.X, a1.Y) && mapa.Aberta(a1.X, a1.Y));
			AfirmarLenha("todas as que tinham caido voltaram, cada uma na celula DELA",
						 LenhaDeTeste(terra) == (0, tocosAgora)
						 && RebrotasDeTeste(terra).All(r => RebrotaDeTeste(terra, r.X, r.Y) is { } rr && rr.Origem == (r.X, r.Y)),
						 $"{LenhaDeTeste(terra)}");

			List<(int X, int Y, TileDeArvore Tile)> brotos = DaZona(noFio, Protocol.LenhaBrotou);
			AfirmarLenha("quem esta na zona e avisado: um pacote `LenhaBrotou` por arvore, com a celula e o tile",
						 brotos.Count == tocosAgora && brotos.Contains((a1.X, a1.Y, a1.Tile)), $"{brotos.Count} pacote(s)");
			List<(int X, int Y, TileDeArvore Tile)> semToco8 = DaZona(noFio, Protocol.LenhaSemToco);
			AfirmarLenha("**O TOCO SOME QUANDO A ARVORE REBROTA**: um `LenhaSemToco` por toco que estava a vista, e nao sobra nenhum a vista na zona",
						 aVistaAntes.Count > 5 && aVistaAntes.Contains((a1.X, a1.Y, a1.Tile)) && semToco8.Count == aVistaAntes.Count
						 && aVistaAntes.All(t => semToco8.Any(s8 => (s8.X, s8.Y) == (t.X, t.Y))) && TocosAVistaDeTeste(terra).Count == 0,
						 $"{aVistaAntes.Count} a vista antes, {semToco8.Count} pacote(s), {TocosAVistaDeTeste(terra).Count} a vista depois");

			pl.Ficha.Recursos = 0;
			SorteioDaLenhaDeTeste = 0.5;
			EscutaDeLenha = [];
			socos = Socar(pl, mapa, a1.X, a1.Y, bp: 5);
			noFio = EscutaDeLenha;
			EscutaDeLenha = null;
			AfirmarLenha("**A REBROTA CAI A SOCO E PAGA DE NOVO**: os mesmos 4 socos de BP 5, um pagamento dentro da faixa, e ela volta a ser toco",
						 socos == 4 && !mapa.BlockedCell(a1.X, a1.Y) && TocoDeTeste(terra, a1.X, a1.Y) && RebrotaDeTeste(terra, a1.X, a1.Y) == null
						 && NaFaixa(pl.Ficha.Recursos), $"{socos} socos, +{pl.Ficha.Recursos:0.##}");
			AfirmarLenha("...e a zona ve a queda e o toco de volta: um `LenhaCaiu` da celula, com poeira, e DEPOIS um `LenhaToco` dela",
						 noFio.Select(f => LerPacoteDeLenha(f.Fio)).Any(p => p.Modo == Protocol.LenhaCaiu && p.Poeira && (p.Arvores[0].X, p.Arvores[0].Y) == (a1.X, a1.Y))
						 && noFio.Count >= 2 && LerPacoteDeLenha(noFio[^1].Fio).Modo == Protocol.LenhaToco
						 && DaZona(noFio, Protocol.LenhaToco).Contains((a1.X, a1.Y, a1.Tile)) && TocoAVistaDeTeste(terra, a1.X, a1.Y));

			(int X, int Y, TileDeArvore Tile) g1 = Arvore();
			pl.Ficha.Recursos = 0;
			SorteioDaLenhaDeTeste = 0;
			RebrotaNaHoraDeTeste = true;
			try
			{
				for (int i = 0; i < 5; i++) { Socar(pl, mapa, g1.X, g1.Y, bp: 5000); PassarALenha(NowMs()); }
				AfirmarLenha("(defeito injetado: a arvore volta no instante em que cai) a MESMA arvore cai e paga cinco vezes seguidas -- o laco que o prazo impede",
							 pl.Ficha.Recursos == 5 * Lenha.RecursosMinimos, $"saldo {pl.Ficha.Recursos:0.##}");
			}
			finally { RebrotaNaHoraDeTeste = false; }

			(int X, int Y, TileDeArvore Tile) h1 = Arvore();
			ArvoreCaidaNaoRebrotaDeTeste = true;
			try { RacharChao(terra, Meio(h1.X, h1.Y), 1_000_000, raio: 0, chance: 1.0); }
			finally { ArvoreCaidaNaoRebrotaDeTeste = false; }
			semToco.Add((h1.X, h1.Y));
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("(defeito injetado: a arvore que cai nao vira toco) passado o prazo ela NAO volta -- o cenario caido de antes, que so o reinicio trazia",
						 !mapa.BlockedCell(h1.X, h1.Y) && !TocoDeTeste(terra, h1.X, h1.Y) && RebrotaDaOrigemDeTeste(terra, h1.X, h1.Y) == null);
			AfirmarLenha("...enquanto a que caiu direito (a rebrota derrubada ha pouco) voltou de novo", RebrotaDaOrigemDeTeste(terra, a1.X, a1.Y) == (a1.X, a1.Y));

			// ============================================================ 9) QUEM CHEGA DEPOIS
			GD.Print("[lenha] -- 9) quem chega depois ve o estado certo --");
			(int X, int Y, TileDeArvore Tile) k1 = Arvore();
			Socar(pl, mapa, k1.X, k1.Y, bp: 5000);   // um toco fresco, pra o retrato ter os dois estados
			EscutaDeLenha = [];
			EscutaDeCenario = [];
			MandarCenario(pl);
			List<(int Para, byte[] Fio)> retratos = [.. EscutaDeLenha.Where(f => f.Para == pl.Id)];
			List<(int Para, byte[] Fio)> estragos = [.. EscutaDeCenario.Where(f => f.Para == pl.Id)];
			EscutaDeLenha = null;
			EscutaDeCenario = null;
			List<(int X, int Y, TileDeArvore Tile)> dePe = RebrotasDeTeste(terra);
			(byte modoDoRetrato, ulong zonaDoRetrato, _, _, _) = retratos.Count == 1 ? LerPacoteDeLenha(retratos[0].Fio) : default;
			AfirmarLenha("QUEM CHEGA recebe, junto do retrato do estrago, UM retrato da lenha (as arvores que voltaram e os tocos) -- com a zona dentro",
						 retratos.Count == 1 && estragos.Count == 1 && modoDoRetrato == Protocol.LenhaRetrato && zonaDoRetrato == terra.Hash,
						 $"{retratos.Count} de lenha, {estragos.Count} de estrago");
			if (retratos.Count == 1 && estragos.Count == 1)
			{
				List<(int X, int Y, TileDeArvore Tile)> noRetrato = LerPacoteDeLenha(retratos[0].Fio).Arvores;
				List<(int X, int Y, TileDeArvore Tile)> tocosNoRetrato = LerPacoteDeLenha(retratos[0].Fio).Tocos, aVista = TocosAVistaDeTeste(terra);
				AfirmarLenha("...com TODAS as que voltaram, celula e tile, sem faltar nem sobrar",
							 dePe.Count > 0 && noRetrato.Count == dePe.Count && noRetrato.ToHashSet().SetEquals(dePe), $"{noRetrato.Count} de {dePe.Count}");
				AfirmarLenha("**...E COM OS TOCOS**: quem chega depois ve o toco de cada arvore caida que ainda nao voltou, com o tile dela (10 bytes por arvore ou toco, mais 18 de cabecalho)",
							 aVista.Count > 0 && aVista.Contains((k1.X, k1.Y, k1.Tile)) && tocosNoRetrato.Count == aVista.Count
							 && tocosNoRetrato.ToHashSet().SetEquals(aVista) && retratos[0].Fio.Length == 18 + 10 * (dePe.Count + aVista.Count),
							 $"{tocosNoRetrato.Count} toco(s) de {aVista.Count}, {retratos[0].Fio.Length} bytes");
				List<(int X, int Y)> caidas = LerPacoteDeCenario(estragos[0].Fio).Celulas;
				AfirmarLenha("...e o retrato do estrago traz o TOCO (terra batida onde a arvore ainda nao voltou) e tambem a origem da que voltou (a terra debaixo dela)",
							 caidas.Contains((k1.X, k1.Y)) && caidas.Contains((a1.X, a1.Y)) && !noRetrato.Any(r => (r.X, r.Y) == (k1.X, k1.Y)));
			}

			RetratoDeLenhaVazioDeTeste = true;
			try
			{
				EscutaDeLenha = [];
				MandarLenha(pl);
				AfirmarLenha($"(defeito injetado: o retrato chega vazio) quem chega nao fica sabendo de nenhuma das {dePe.Count} arvores que voltaram -- e andaria por dentro delas --, nem ve toco nenhum",
							 EscutaDeLenha.Count == 1 && LerPacoteDeLenha(EscutaDeLenha[0].Fio).Arvores.Count == 0
							 && LerPacoteDeLenha(EscutaDeLenha[0].Fio).Tocos.Count == 0);
			}
			finally { RetratoDeLenhaVazioDeTeste = false; EscutaDeLenha = null; }

			pl.Zone = namek;
			try
			{
				EscutaDeLenha = [];
				MandarLenha(pl);
				(byte modoN, ulong zonaN, List<(int X, int Y, TileDeArvore Tile)> arvoresN, _, List<(int X, int Y, TileDeArvore Tile)> tocosN) =
					LerPacoteDeLenha(EscutaDeLenha[0].Fio);
				AfirmarLenha("zona sem lenha: o retrato sai VAZIO, mas sai, e com a zona -- e o que diz ao cliente que as listas do planeta de onde ele veio nao valem mais",
							 modoN == Protocol.LenhaRetrato && zonaN == namek.Hash && arvoresN.Count == 0 && tocosN.Count == 0);
			}
			finally { pl.Zone = terra; EscutaDeLenha = null; }

			// ============================================================ 10) LUGAR OCUPADO
			GD.Print("[lenha] -- 10) lugar ocupado: ela renasce noutro ponto --");
			BlocoDef? parede = Bloco(ClasseDeBloco.Parede), piso = Bloco(ClasseDeBloco.Piso);
			AfirmarLenha("(montagem) o catalogo de blocos tem parede e piso", parede != null && piso != null);
			if (parede == null || piso == null) return;
			// construir pode custar (zeni ou recurso): o host fica rico pela duracao da secao
			pl.Ficha.Zeni += 1e9;

			// Onde renasceu a arvore desta origem, e a que distancia.
			(int X, int Y)? Onde((int X, int Y, TileDeArvore Tile) a) => RebrotaDaOrigemDeTeste(terra, a.X, a.Y);
			int Longe((int X, int Y) p, (int X, int Y, TileDeArvore Tile) a) => Math.Max(Math.Abs(p.X - a.X), Math.Abs(p.Y - a.Y));
			bool Clareira((int X, int Y) p)
			{
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						if ((dx != 0 || dy != 0) && !mapa.ServeDeChao(p.X + dx, p.Y + dy)) return false;
				return true;
			}
			(int X, int Y) berco = _catalogo?.Get(terra)?.Berco is { } bc ? (bc.Cx, bc.Cy) : (-99, -99);

			// (a) a PAREDE de alguem em cima do toco
			(int X, int Y, TileDeArvore Tile) i1 = Arvore();
			Socar(pl, mapa, i1.X, i1.Y, bp: 5000);
			bool haviaToco = TocoAVistaDeTeste(terra, i1.X, i1.Y);
			pl.Ficha.Recursos = 1e9;
			pl.Pos = AoSul(i1.X, i1.Y);
			ComandoDeBloco(pl, Protocol.BlocoErguer, i1.X, i1.Y, parede.Numero, "");
			AfirmarLenha("(montagem) o jogador ergue uma PAREDE em cima do toco (a terra batida e chao: constroi-se nela; o toco nao barra)",
						 haviaToco && BlocoEm(terra, i1.X, i1.Y) != null && mapa.BlockedCell(i1.X, i1.Y));

			// UMA PASSADA da lenha (um segundo de relogio): o zelador ve a parede em cima do toco.
			void UmaPassada() { AdiantoDoRelogioDeTeste += MsEntrePassadasDaLenha + 100; TickDaLenha(); }
			EscutaDeLenha = [];
			UmaPassada();
			List<(int X, int Y, TileDeArvore Tile)> semToco10 = DaZona(EscutaDeLenha, Protocol.LenhaSemToco);
			EscutaDeLenha = null;
			AfirmarLenha("**CONSTRUIRAM EM CIMA: O TOCO SAI DA VISTA** na passada seguinte (um `LenhaSemToco` da celula) -- so o desenho: a arvore continua caida, com o prazo dela",
						 !TocoAVistaDeTeste(terra, i1.X, i1.Y) && TocoDeTeste(terra, i1.X, i1.Y)
						 && semToco10.Count == 1 && (semToco10[0].X, semToco10[0].Y) == (i1.X, i1.Y), $"{semToco10.Count} pacote(s)");
			PassarOTempo(depoisDoPrazo);
			(int X, int Y)? o1 = Onde(i1);
			AfirmarLenha("**LUGAR OCUPADO -> RENASCE NOUTRO PONTO**: a arvore volta, de pe e barrando, e NAO em cima da parede",
						 o1 is { } p1 && p1 != (i1.X, i1.Y) && mapa.BlockedCell(p1.X, p1.Y) && mapa.Rebrotada(p1.X, p1.Y)
						 && BlocoEm(terra, i1.X, i1.Y) != null && !mapa.Rebrotada(i1.X, i1.Y), $"origem ({i1.X},{i1.Y}) -> {o1}");
			if (o1 is { } n1)
			{
				AfirmarLenha($"...no chao livre mais perto (a ate {Lenha.RaioDeReplantio} tiles), numa CLAREIRA: as oito celulas em volta sao chao -- arvore nova nunca fecha uma passagem",
							 Longe(n1, i1) <= Lenha.RaioDeReplantio && Clareira(n1), $"a {Longe(n1, i1)} tile(s)");
				AfirmarLenha("...em chao que nunca foi parede, a ceu aberto, sem nada de ninguem, e longe do berco (onde quem nasce aparece)",
							 !mapa.BloqueadaNoArquivo(n1.X, n1.Y) && !CenarioJaCaiu(terra, n1.X, n1.Y) && !mapa.NasceuDentro(n1.X, n1.Y)
							 && BlocoEm(terra, n1.X, n1.Y) == null && ObraNaCelula(terra, n1.X, n1.Y) == null
							 && Math.Max(Math.Abs(n1.X - berco.X), Math.Abs(n1.Y - berco.Y)) > FolgaDaChegada);
				AfirmarLenha("...com o tile da arvore de ORIGEM (a mesma arvore, noutro chao)", RebrotaDeTeste(terra, n1.X, n1.Y)?.Tile == i1.Tile);

				// EM CIMA DE ARVORE DE PE NAO SE CONSTROI -- nem na que mudou de lugar, nem na que voltou na origem.
				pl.Pos = AoSul(n1.X, n1.Y);
				RecusaDeBloco naMudada = RecusaDeErguer(pl, piso, n1.X, n1.Y);
				pl.Pos = AoSul(a1.X, a1.Y);
				RecusaDeBloco naOrigem = RecusaDeErguer(pl, parede, a1.X, a1.Y);
				AfirmarLenha("em cima de arvore que rebrotou NAO se ergue bloco (\"tem cenario de pe aqui\"), nem se assenta maquina -- derruba-se primeiro",
							 naMudada == RecusaDeBloco.Cenario && naOrigem == RecusaDeBloco.Cenario
							 && Jandirus.Core.Tech.Assentamento.DoLugar(mapa, pl.Pos, a1.X, a1.Y) != Jandirus.Core.Tech.RecusaDeAssento.Pode,
							 $"{naMudada} / {naOrigem}");

				pl.Ficha.Recursos = 0;
				SorteioDaLenhaDeTeste = 0.5;
				socos = Socar(pl, mapa, n1.X, n1.Y, bp: 5000);
				AfirmarLenha("a arvore que mudou de lugar cai a soco e paga como qualquer outra; o chao dela volta a ser chao comum (nao vira estrago), e o prazo e o da ORIGEM",
							 socos == 1 && !mapa.BlockedCell(n1.X, n1.Y) && !CenarioJaCaiu(terra, n1.X, n1.Y) && TocoDeTeste(terra, i1.X, i1.Y)
							 && NaFaixa(pl.Ficha.Recursos), $"+{pl.Ficha.Recursos:0.##}");
				AfirmarLenha("...e o TOCO fica ONDE ELA CAIU (o chao em que estava de pe), e nao na origem -- que esta debaixo da parede de alguem",
							 TocoAVistaDeTeste(terra, n1.X, n1.Y) && !TocoAVistaDeTeste(terra, i1.X, i1.Y));
			}
			pl.Pos = AoSul(i1.X, i1.Y);
			ComandoDeBloco(pl, Protocol.BlocoDesmanchar, i1.X, i1.Y, 0, "");
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("desmanchada a parede, a arvore volta pro lugar DELA -- e o toco que tinha ficado no outro chao some com a volta",
						 BlocoEm(terra, i1.X, i1.Y) == null && Onde(i1) == (i1.X, i1.Y) && mapa.BlockedCell(i1.X, i1.Y)
						 && (o1 is not { } ondeCaiu || !TocoAVistaDeTeste(terra, ondeCaiu.X, ondeCaiu.Y)), $"{Onde(i1)}");

			// (b) um PISO (nao barra) em cima do toco
			(int X, int Y, TileDeArvore Tile) i2 = Arvore();
			Socar(pl, mapa, i2.X, i2.Y, bp: 5000);
			pl.Ficha.Recursos = 1e9;
			pl.Pos = AoSul(i2.X, i2.Y);
			ComandoDeBloco(pl, Protocol.BlocoErguer, i2.X, i2.Y, piso.Numero, "");
			TocoFicaSobAObraDeTeste = true;
			try
			{
				UmaPassada();
				AfirmarLenha("(defeito injetado: o zelador nao olha os tocos) o PISO esta posto e o toco continua a vista, desenhado por cima da base de alguem",
							 BlocoEm(terra, i2.X, i2.Y) != null && TocoAVistaDeTeste(terra, i2.X, i2.Y));
			}
			finally { TocoFicaSobAObraDeTeste = false; }
			UmaPassada();
			AfirmarLenha("sem o defeito, a passada seguinte tira o toco de debaixo do piso", !TocoAVistaDeTeste(terra, i2.X, i2.Y) && TocoDeTeste(terra, i2.X, i2.Y));
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("um PISO em cima do toco (nao barra, mas e base de alguem): a arvore tambem vai nascer noutro lugar",
						 BlocoEm(terra, i2.X, i2.Y) != null && Onde(i2) is { } p2 && p2 != (i2.X, i2.Y) && !mapa.BlockedCell(i2.X, i2.Y),
						 $"origem ({i2.X},{i2.Y}) -> {Onde(i2)}");

			// (c) uma MAQUINA em cima do toco
			(int X, int Y, TileDeArvore Tile) i3 = Arvore();
			Socar(pl, mapa, i3.X, i3.Y, bp: 5000);
			var maquina = new Obra { Id = 990_701, Tipo = "Research_Station", X = Em(i3.X, i3.Y).X, Y = Em(i3.X, i3.Y).Y, DonoNome = pl.Name, Aparafusada = true };
			maquina.PorZona(terra);
			_noChao.Add(maquina);
			obrasDaBancada.Add(maquina);
			AplicarColisaoDasObras(terra);   // o que toda instalacao de verdade faz depois de por a obra no chao

			// ---- A MAQUINA NO LUGAR DA ARVORE CAIDA BARRA O CORPO (`ZoneCollision.BlockedCell`) ----
			// A celula e parede no ARQUIVO (era a arvore) e esta ABERTA (ela caiu). Ate 2026-10-10 a resposta ali era
			// "livre" sem olhar a camada das obras, e o corpo atravessava a maquina -- nas duas pontas, que a conta e
			// do Core. Quem anda e o `MoveRules.Advance` de producao: tres segundos de passo pro norte, a partir da
			// celula de chao ao sul dela.
			Vec2 Subir(Vec2 de)
			{
				Vec2 p = de;
				for (int i = 0; i < 90; i++) p = MoveRules.Advance(p, new Vec2(0, -1), Protocol.TickSeconds, 1f, mapa, out _);
				return p;
			}
			float TopoDosPes(Vec2 corpo) => corpo.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH;
			float peDaMaquina = (i3.Y + 1) * T;   // a beirada de baixo da celula dela
			Vec2 doSul = AoSul(i3.X, i3.Y), parou = Subir(doSul);
			AfirmarLenha("**A MAQUINA ASSENTADA NO TOCO BARRA**: a celula e parede do arquivo (a arvore) aberta pela queda, e mesmo assim o corpo que anda contra a maquina PARA nela -- tres segundos de passo e ele nao entra",
						 mapa.BloqueadaNoArquivo(i3.X, i3.Y) && mapa.Aberta(i3.X, i3.Y) && mapa.BlockedCell(i3.X, i3.Y)
						 && TopoDosPes(parou) >= peDaMaquina - 0.01f && parou.Y < doSul.Y - 1 && !MoveRules.Occupied(mapa, parou),
						 $"saiu de y={doSul.Y:0}, parou em y={parou.Y:0} (pes ate {TopoDosPes(parou):0}); a maquina acaba em y={peDaMaquina:0}");
			AfirmarLenha("...e quem pergunta \"cabe um corpo aqui?\" ouve que nao: a celula deixou de servir de chao (pouso, teleporte, outra maquina por cima)",
						 !mapa.ServeDeChao(i3.X, i3.Y) && MoveRules.Occupied(mapa, Em(i3.X, i3.Y))
						 && Jandirus.Core.Tech.Assentamento.DoLugar(mapa, doSul, i3.X, i3.Y) == Jandirus.Core.Tech.RecusaDeAssento.DentroDeParede);

			ZoneCollision.CelulaAbertaIgnoraObraDeTeste = true;
			try
			{
				Vec2 atravessou = Subir(doSul);
				AfirmarLenha("(defeito injetado: a celula aberta nao olha a obra) o MESMO corpo, pelo MESMO caminho, ENTRA na celula da maquina e a atravessa -- o estado de antes",
							 !mapa.BlockedCell(i3.X, i3.Y) && TopoDosPes(atravessou) < peDaMaquina - 1,
							 $"parou em y={atravessou.Y:0} (pes ate {TopoDosPes(atravessou):0}); a maquina acaba em y={peDaMaquina:0}");
			}
			finally { ZoneCollision.CelulaAbertaIgnoraObraDeTeste = false; }

			// ...e o que NAO muda: a celula caida SEM obra densa continua chao (a do piso, logo acima), e a parede do
			// arquivo que ninguem abriu continua parede (a arvore em que a bola de ki morreu, que nunca caiu).
			AfirmarLenha("...e o resto fica como era: a celula de arvore caida SEM maquina continua aberta (anda-se por ela), e a arvore do mapa que esta de pe continua parede",
						 !mapa.BlockedCell(i2.X, i2.Y) && mapa.Aberta(i2.X, i2.Y) && mapa.BloqueadaNoArquivo(i2.X, i2.Y)
						 && mapa.BlockedCell(c0.X, c0.Y) && mapa.BloqueadaNoArquivo(c0.X, c0.Y) && !mapa.Aberta(c0.X, c0.Y));

			UmaPassada();
			bool tocoSobAMaquina = TocoAVistaDeTeste(terra, i3.X, i3.Y);
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("uma MAQUINA assentada em cima do toco: idem -- o toco sai da vista, e a arvore nasce noutro lugar",
						 ObraNaCelula(terra, i3.X, i3.Y) == maquina && !tocoSobAMaquina && Onde(i3) is { } p3 && p3 != (i3.X, i3.Y),
						 $"origem ({i3.X},{i3.Y}) -> {Onde(i3)}");

			// (d) ALGUEM em pe em cima do toco: espera
			(int X, int Y, TileDeArvore Tile) i4 = Arvore();
			Socar(pl, mapa, i4.X, i4.Y, bp: 5000);
			pl.Pos = AoSul(a1.X, a1.Y);
			npc.Pos = Em(i4.X, i4.Y);
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("ALGUEM EM PE em cima do toco: a arvore ESPERA -- gente passa, e ela nao muda de lugar por isso (nem nasce em cima de ninguem)",
						 TocoDeTeste(terra, i4.X, i4.Y) && Onde(i4) == null && !mapa.BlockedCell(i4.X, i4.Y));
			npc.Pos = AoSul(a2.X, a2.Y);
			PassarOTempo(Lenha.NovaTentativaS);
			AfirmarLenha($"saiu de cima: na tentativa seguinte ({Lenha.NovaTentativaS:0} s depois) ela volta, no lugar dela",
						 Onde(i4) == (i4.X, i4.Y) && mapa.BlockedCell(i4.X, i4.Y), $"{Onde(i4)}");

			// (e) o defeito: nasce por cima
			(int X, int Y, TileDeArvore Tile) i5 = Arvore();
			Socar(pl, mapa, i5.X, i5.Y, bp: 5000);
			pl.Pos = AoSul(i5.X, i5.Y);
			ComandoDeBloco(pl, Protocol.BlocoErguer, i5.X, i5.Y, parede.Numero, "");
			RebrotaSemOlharOLugarDeTeste = true;
			try
			{
				PassarOTempo(depoisDoPrazo);
				AfirmarLenha("(defeito injetado: a arvore volta na origem sem olhar o que ha la) ela nasce POR CIMA da parede de alguem",
							 BlocoEm(terra, i5.X, i5.Y) != null && Onde(i5) == (i5.X, i5.Y), $"{Onde(i5)}");
			}
			finally { RebrotaSemOlharOLugarDeTeste = false; }

			// (f) O QUE ESTAVA EM CIMA SAI ANTES DE A ARVORE VOLTAR: o toco volta. A nave parada e o caso que pede a
			// regra (pousa, decola, e a arvore continua caida ali); o piso e o mesmo caminho pelo comando de producao.
			(int X, int Y, TileDeArvore Tile) i6 = Arvore();
			Socar(pl, mapa, i6.X, i6.Y, bp: 5000);
			var nave = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = Em(i6.X, i6.Y).X, Y = Em(i6.X, i6.Y).Y };
			nave.PorZona(terra);
			_naves.Add(nave);
			try
			{
				UmaPassada();
				AfirmarLenha("(montagem) uma NAVE parada em cima do toco: ele sai da vista, como debaixo de parede, piso e maquina",
							 NaveNaCelula(terra, i6.X, i6.Y) == nave && !TocoAVistaDeTeste(terra, i6.X, i6.Y) && TocoDeTeste(terra, i6.X, i6.Y));
			}
			finally { _naves.Remove(nave); }   // a nave decola

			TocoNaoVoltaDeTeste = true;
			try
			{
				UmaPassada();
				AfirmarLenha("(defeito injetado: o toco que saiu da vista nao volta) a nave decolou, o chao esta livre, a arvore continua caida ali -- e nao ha toco nenhum: terra batida de onde uma arvore vai brotar do nada",
							 NaveNaCelula(terra, i6.X, i6.Y) == null && !TocoAVistaDeTeste(terra, i6.X, i6.Y) && TocoDeTeste(terra, i6.X, i6.Y));
			}
			finally { TocoNaoVoltaDeTeste = false; }

			EscutaDeLenha = [];
			UmaPassada();
			List<(int X, int Y, TileDeArvore Tile)> voltou = DaZona(EscutaDeLenha, Protocol.LenhaToco);
			EscutaDeLenha = null;
			AfirmarLenha("**O QUE ESTAVA EM CIMA SAIU: O TOCO VOLTA** na passada seguinte (um `LenhaToco` da celula, com o tile da arvore) -- ele esta a vista enquanto o chao dele esta livre, ate a arvore rebrotar",
						 TocoAVistaDeTeste(terra, i6.X, i6.Y) && voltou.Count == 1 && voltou[0] == (i6.X, i6.Y, i6.Tile), $"{voltou.Count} pacote(s)");

			pl.Ficha.Recursos = 1e9;
			pl.Pos = AoSul(i6.X, i6.Y);
			ComandoDeBloco(pl, Protocol.BlocoErguer, i6.X, i6.Y, piso.Numero, "");
			UmaPassada();
			bool sumiuSobOPiso = BlocoEm(terra, i6.X, i6.Y) != null && !TocoAVistaDeTeste(terra, i6.X, i6.Y);
			ComandoDeBloco(pl, Protocol.BlocoDesmanchar, i6.X, i6.Y, 0, "");
			UmaPassada();
			bool voltouSemOPiso = BlocoEm(terra, i6.X, i6.Y) == null && TocoAVistaDeTeste(terra, i6.X, i6.Y);
			PassarOTempo(depoisDoPrazo);
			AfirmarLenha("...e com bloco de base e igual: o PISO erguido em cima o esconde, desmanchado o piso ele esta de volta -- e a arvore, no prazo, rebrota ALI MESMO (o toco dizia a verdade)",
						 sumiuSobOPiso && voltouSemOPiso && Onde(i6) == (i6.X, i6.Y) && mapa.BlockedCell(i6.X, i6.Y)
						 && !TocoAVistaDeTeste(terra, i6.X, i6.Y), $"sumiu {sumiuSobOPiso}, voltou {voltouSemOPiso}, rebrotou em {Onde(i6)}");

			// ============================================================ 11) A REBROTA NO FUNIL DE QUEDA
			GD.Print("[lenha] -- 11) a rebrota cai pelo funil de sempre, e nao so a soco --");
			pl.Pos = AoSul(a1.X, a1.Y);
			antes = pl.Ficha.Recursos;
			RebrotaForaDoFunilDeTeste = true;
			try
			{
				rachadas = RacharChao(terra, Meio(i4.X, i4.Y), 1_000_000, raio: 0, chance: 1.0);
				AfirmarLenha("(defeito injetado: a rebrota fica fora do funil de queda) o chao racha em cima dela e ela continua de pe -- uma arvore que nada derruba",
							 rachadas == 0 && mapa.BlockedCell(i4.X, i4.Y));
			}
			finally { RebrotaForaDoFunilDeTeste = false; }
			rachadas = RacharChao(terra, Meio(i4.X, i4.Y), 1_000_000, raio: 0, chance: 1.0);
			AfirmarLenha("o chao que racha em cima de uma REBROTA a derruba: ela volta a toco, e ninguem recebe",
						 rachadas == 1 && !mapa.BlockedCell(i4.X, i4.Y) && TocoDeTeste(terra, i4.X, i4.Y) && pl.Ficha.Recursos == antes);
			if (Onde(i2) is { } mudada)
			{
				caiu = DerrubarCenario(terra, Meio(mudada.X, mudada.Y));
				AfirmarLenha("o arremesso contra a que MUDOU DE LUGAR idem -- e o chao dela, que nunca foi parede, nao entra no estrago",
							 caiu && !mapa.BlockedCell(mudada.X, mudada.Y) && !CenarioJaCaiu(terra, mudada.X, mudada.Y) && TocoDeTeste(terra, i2.X, i2.Y));
			}

			// ============================================================ 12) O CENARIO REFEITO
			GD.Print("[lenha] -- 12) o admin refaz o cenario: as arvores do mapa voltam, e a lenha esquece --");
			(int X, int Y)? mudou3 = Onde(i3);
			bool haviaTocoAntesDoAdmin = TocosAVistaDeTeste(terra).Count > 0;
			AdminConsertarCenario(pl);
			PassarALenha(NowMs());
			AfirmarLenha("O ADMIN REFAZ O CENARIO DA ZONA: as arvores do mapa estao de pe de novo, e nao sobra toco (nem a vista, nem esperando) nem rebrota por cima delas",
						 LenhaDeTeste(terra) == (0, 0) && TocosAVistaDeTeste(terra).Count == 0 && haviaTocoAntesDoAdmin
						 && ArvoreDoMapaDePe(terra, mapa, a1.X, a1.Y) && mapa.BlockedCell(a1.X, a1.Y)
						 && !mapa.Rebrotada(a1.X, a1.Y), $"{LenhaDeTeste(terra)}");
			AfirmarLenha("...inclusive a que tinha mudado de lugar: o chao em que ela estava volta a ser chao",
						 mudou3 is { } m3 && !mapa.BlockedCell(m3.X, m3.Y) && !mapa.Rebrotada(m3.X, m3.Y), $"{mudou3}");
			semToco.Clear();   // o verb do admin ja devolveu tudo o que estava no estrago da Terra

			// ============================================================ 13) O MUNDO SORTEADO
			GD.Print("[lenha] -- 13) o planeta sorteado: o novato derruba, e o mundo que renasce lembra --");
			LenhaNoMundoSorteado(pl, out mundo);
		}
		catch (Exception e)
		{
			AfirmarLenha("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Lenha.TroncoComoParedeDeTeste = Lenha.PagaEmCombateDeTeste = Lenha.PagaQualquerUmDeTeste = false;
			ArvoreCaidaNaoRebrotaDeTeste = RebrotaNaHoraDeTeste = RebrotaSemOlharOLugarDeTeste = false;
			RetratoDeLenhaVazioDeTeste = RebrotaForaDoFunilDeTeste = MundoRenasceSemOEstragoDeTeste = false;
			ApanharDoMundoEBrigaDeTeste = TocoMudoDeTeste = TocoFicaSobAObraDeTeste = TocoNaoVoltaDeTeste = false;
			ZoneCollision.CelulaAbertaIgnoraObraDeTeste = false;
			ArvoresDoMapa.SoAPastaDeTeste = false;
			ArteDoToco.CascaPadraoDeTeste = false;
			Lenha.EsmagadoContaComoBriga = true;
			SorteioDaLenhaDeTeste = null;
			EscutaDeLenha = null;
			EscutaDeCenario = null;

			// O RELOGIO VOLTA, e a espera da passada com ele (ela foi marcada com o relogio adiantado).
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_proximaLenha = 0;

			// O MUNDO VOLTA: as maquinas e os blocos da bancada saem, as arvores derrubadas ficam de pe.
			foreach (Obra o in obrasDaBancada) _noChao.Remove(o);
			if (obrasDaBancada.Count > 0) AplicarColisaoDasObras(terra);
			ZerarBases();
			foreach (BlocoDePe b in blocosDeAntes) GuardarBloco(b);
			foreach (BaseDaZona z in _bases.Values) AssentarBase(z);
			_basesSujas = false;

			DesfazerALenhaDeTeste(terra);
			foreach ((int x, int y) in semToco) DesfazerQueda(terra, x, y);
			if (mundo is { } mg)
			{
				DesfazerALenhaDeTeste(mg);
				_cenarioCaido.Remove(ChaveDeEstado(mg));
			}

			pl.Zone = terra;
			pl.Pos = posDeAntes;
			pl.Facing = olharDeAntes;
			pl.Cerebro = null;
			pl.Ficha.Recursos = recursosDeAntes;
			pl.Ficha.Zeni = zeniDeAntes;
			pl.Ficha.expressedBP = bpDeAntes;
			pl.Combate.EmCombate = tagDeAntes;
			pl.Combate.LutandoDeVerdade = lutaDeAntes;
			pl.Combate.BrigandoComAlguem = brigaDeAntes;
			pl.Combate.Recarga = recargaDeAntes;

			if (vizinho != null) vizinho.Peer = null;   // o `Peer` emprestado volta antes de o corpo sair
			LimparTudoDaBancada();
			if (entrouPelaBancada) daZona.Remove(pl);   // o `Entrar` o poe de volta, na linha dele

			// O PLACAR SAI DAQUI: a bancada tem saidas no meio (montagem que falhou), e sem ele o lancador espera o prazo inteiro.
			GD.Print($"[lenha] ================ {_lhOk} OK, {_lhFalhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// A SECAO DO PLANETA SORTEADO -- o lugar pra que este pedido existe. O mundo nasce pelo caminho de producao
	/// (`MundoDaZona` encomenda, `TickDasGeracoes` colhe); o host soca la com a zona dele trocada na mao, sem viajar
	/// (o que se mede e o servidor). Descarregar o mundo e o que o `PodarZonasVazias` faz com um mundo vazio.
	/// </summary>
	private void LenhaNoMundoSorteado(ServerPlayer pl, out ZoneKey? mundo)
	{
		mundo = null;
		const int T = ZoneCollision.TileSize;
		ZoneKey terra = pl.Zone;

		if (AcharPlanetaGerado(SeedDoUniverso) is not { } planeta || ZonaGeradaPronta(planeta) is not { } zg
			|| MapaDaZonaOuCatalogo(zg) is not { } mg)
		{
			AfirmarLenha("(montagem) ha um mundo sorteado na carta, e ele nasceu", false);
			return;
		}
		mundo = zg;

		Vec2 AoSul(int cx, int cy) => new(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
		int Socar(ZoneCollision m, int cx, int cy, double bp)
		{
			pl.Pos = AoSul(cx, cy);
			pl.Facing = Facing.North;
			pl.Ficha.expressedBP = bp;
			int n = 0;
			while (n < 12 && m.BlockedCell(cx, cy)) { SocarCenario(pl); n++; }
			return n;
		}

		try
		{
			pl.Zone = zg;
			pl.Combate.EmCombate = 0;
			ArvoresDoMapa? arvores = ArvoresDa(zg);
			AfirmarLenha("MUNDO SORTEADO: na colheita do mundo o servidor anota as arvores dele (a cobertura que o gerador pos)",
						 arvores is { Total: > 0 }, $"{arvores?.Total ?? 0} arvores em {planeta.Nome} ({mg.Width}x{mg.Height})");
			if (arvores == null) return;

			Vec2 pouso = ChegadaDaZonaGerada(zg) ?? new Vec2(mg.Width * T / 2f, mg.Height * T / 2f);
			var usadas = new HashSet<(int X, int Y)>();
			(int X, int Y, TileDeArvore Tile)? t1 = ArvoreSocavelPertoDeTeste(zg, pouso, usadas);
			if (t1 is { } u1) usadas.Add((u1.X, u1.Y));
			(int X, int Y, TileDeArvore Tile)? t2 = ArvoreSocavelPertoDeTeste(zg, pouso, usadas);
			AfirmarLenha("(montagem) ha duas arvores socaveis perto do pouso", t1 != null && t2 != null);
			if (t1 is not { } a || t2 is not { } b) return;

			pl.Ficha.Recursos = 0;
			SorteioDaLenhaDeTeste = null;
			int socos = Socar(mg, a.X, a.Y, bp: 3);
			// O host esta aqui com a zona trocada na mao: a gravidade dele continua a da Terra, e ele nao e esmagado. O
			// novato que o mundo ESMAGA nao recebe (secao 4, e a `--diaglenha` num mundo de 2x de verdade).
			AfirmarLenha("**O NOVATO NUM PLANETA SORTEADO** (BP 3, num chao que o corpo dele aguenta): cinco socos, a arvore cai, ele recebe e fica o toco",
						 !Esmagamento.Esmaga(pl.Ficha) && socos == 5 && !mg.BlockedCell(a.X, a.Y) && TocoDeTeste(zg, a.X, a.Y) && TocoAVistaDeTeste(zg, a.X, a.Y)
						 && pl.Ficha.Recursos >= Lenha.RecursosMinimos && pl.Ficha.Recursos <= Lenha.RecursosMaximos,
						 $"{socos} socos, +{pl.Ficha.Recursos:0.##}");
			AfirmarLenha("...e o tile dela e o da paleta do bioma, resolvido pelo indice de tiles (o que o cliente pinta)", a.Tile.TemDesenho, $"{a.Tile}");

			AdiantoDoRelogioDeTeste += (long)((Lenha.RebrotaMaxS + 5) * 1000);
			PassarALenha(NowMs());
			AfirmarLenha("passado o prazo ela rebrota la tambem, no lugar dela", RebrotaDaOrigemDeTeste(zg, a.X, a.Y) == (a.X, a.Y) && mg.BlockedCell(a.X, a.Y));

			Socar(mg, b.X, b.Y, bp: 5000);   // a segunda fica como TOCO
			AfirmarLenha("(montagem) o mundo tem uma rebrota de pe e um toco", LenhaDeTeste(zg) == (1, 1), $"{LenhaDeTeste(zg)}");

			// ---- o mundo esvazia, e descarregado, e alguem volta ----
			_zonasGeradas.Remove(zg.Hash);
			PassarALenha(NowMs());
			AfirmarLenha("(montagem) o mundo foi descarregado: nao ha mapa, o plano de arvores saiu com ele, e a lenha dele ficou guardada",
						 MapaDaZonaOuCatalogo(zg) == null && ArvoresDa(zg) == null && LenhaDeTeste(zg) == (1, 1));

			MundoRenasceSemOEstragoDeTeste = true;
			try
			{
				ZonaGeradaPronta(planeta);
				AfirmarLenha("(defeito injetado: o mundo renasce sem o estrago anotado) o toco esta no retrato de cenario e FECHADO no mapa novo -- a parede de pe no servidor onde o cliente a abre",
							 MapaDaZonaOuCatalogo(zg) is { } torto && CenarioJaCaiu(zg, b.X, b.Y) && torto.BlockedCell(b.X, b.Y));
			}
			finally { MundoRenasceSemOEstragoDeTeste = false; }

			_zonasGeradas.Remove(zg.Hash);
			ZonaGeradaPronta(planeta);
			ZoneCollision? mg2 = MapaDaZonaOuCatalogo(zg);
			AfirmarLenha("**O MUNDO RENASCE COM O ESTADO QUE TINHA**: o mapa e outro objeto, e nele o toco esta ABERTO (o estrago anotado voltou a valer)",
						 mg2 != null && !ReferenceEquals(mg, mg2) && !mg2.BlockedCell(b.X, b.Y) && mg2.Aberta(b.X, b.Y));
			AfirmarLenha("...a rebrota esta de pe e barrando no mapa novo, e as arvores do mundo voltaram a ser conhecidas",
						 mg2 != null && mg2.Rebrotada(a.X, a.Y) && mg2.BlockedCell(a.X, a.Y) && ArvoresDa(zg)?.Total == arvores.Total);

			// ---- e o toco que venceu com o mundo descarregado rebrota NA COLHEITA, antes de alguem pousar ----
			_zonasGeradas.Remove(zg.Hash);
			AdiantoDoRelogioDeTeste += (long)((Lenha.RebrotaMaxS + 5) * 1000);
			PassarALenha(NowMs());
			AfirmarLenha("com o mundo descarregado o toco ESPERA, mesmo vencido (sem mapa nao ha como saber se o lugar esta livre)",
						 TocoDeTeste(zg, b.X, b.Y) && LenhaDeTeste(zg) == (1, 1));
			ZonaGeradaPronta(planeta);
			AfirmarLenha("...e rebrota na COLHEITA do mundo, antes de alguem pousar: quem chega recebe o retrato pronto",
						 RebrotaDaOrigemDeTeste(zg, b.X, b.Y) == (b.X, b.Y) && MapaDaZonaOuCatalogo(zg)?.BlockedCell(b.X, b.Y) == true
						 && LenhaDeTeste(zg) == (0, 2), $"{LenhaDeTeste(zg)}");
		}
		finally
		{
			MundoRenasceSemOEstragoDeTeste = false;
			pl.Zone = terra;
		}
	}
}
