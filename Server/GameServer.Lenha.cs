using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A LENHA NO SERVIDOR -- a arvore que cai a soco, paga um pouco de recurso e rebrota.
///
/// O dono (2026-10-10): *"derrubar arvores no soco (durante combate n conta) vc ganha uma quantidade pequena de
/// resources pra deixar o jogo mais minecraft derrubando arvore pra construir coisas. essas arvores dps de um tempo
/// voltam ou spawnam em outro local."* As contas (quantos socos, quanto rende, em quanto tempo volta) estao em
/// <see cref="Lenha"/>; "onde ha arvore" e o <see cref="ArvoresDoMapa"/>. Aqui mora o que mexe no mundo.
///
/// ============================ COMO A ARVORE JA EXISTIA, E O QUE NAO MUDOU ============================
/// A arvore do mapa e a do planeta sorteado sao TILE: uma celula que bloqueia, igual a um muro. Ela ja caia -- pelo
/// soco (`SocarCenario`, a regra do turf: `expressedBP >= 20` e 34% por golpe), pelo corpo arremessado, pelo chao
/// que racha, pelas tecnicas -- sempre pelo `DerrubarCelula`: a celula entra no estrago da zona (`_cenarioCaido`),
/// abre nos dois mapas, e o cliente apaga o tile e pinta terra batida. E ficava assim ate o servidor reiniciar
/// (o estrago mora em memoria; so o verb de admin o desfaz).
///
/// TUDO ISSO CONTINUA. A arvore derrubada continua sendo cenario caido, pelo mesmo funil, com a mesma poeira e a
/// mesma terra batida, no retrato de quem chega e no cache de zona do cliente. O que este arquivo acrescenta:
///
///   1. O SOCO NUMA ARVORE tem regra propria (<see cref="SocarArvore"/>): o tronco cede em ate cinco golpes de
///      qualquer um -- sem o limiar de 20 nem o sorteio --, e quem derruba recebe.
///   2. TODA ARVORE QUE CAI VIRA TOCO (<see cref="AArvoreDoMapaCaiu"/>), caia como cair: o toco tem prazo.
///   3. O TOCO REBROTA (<see cref="TickDaLenha"/>): no lugar dele, ou no chao livre mais perto se o lugar foi
///      tomado. A arvore que volta e uma REBROTA.
/// ====================================================================================================
///
/// ============================ POR QUE A REBROTA NAO "DESFAZ" O ESTRAGO ============================
/// Desfazer seria tirar a celula do `_cenarioCaido` e mandar o cliente repor o que havia nela. O cliente nao
/// guarda o que apagou (o `Restaurar` do admin recarrega a cena inteira por isso), a cena de uma zona fica
/// guardada no cache dele com os buracos, e a arvore que renasce NOUTRO lugar nao tem celula de arquivo pra
/// reabrir. Entao a rebrota e uma coisa NOVA por cima do mundo, como a porta e o bloco erguido: uma lista do
/// servidor (celula + tile), um retrato pra quem chega e um aviso pra zona a cada mudanca. No mapa de colisao ela
/// e uma camada propria (`ZoneCollision.Rebrotar`), nas duas pontas; na tela e o MESMO tile da arvore que caiu,
/// numa camada do cliente que ordena por Y junto com os corpos (`Client/RebrotasNaTela.cs`).
///
/// A identidade de uma arvore e a celula em que o mapa a pos (a ORIGEM). Ela esta sempre num de tres estados: de
/// pe no mapa; TOCO (caida, com prazo); ou REBROTA (de pe de novo, na origem ou perto). Quem derruba a rebrota
/// devolve a arvore a toco. O numero de arvores do planeta nao muda.
/// ==================================================================================================
///
/// ============================ "DURANTE COMBATE N CONTA": DOIS MOTIVOS, DUAS FRASES ============================
/// Quem paga e o <see cref="PagarALenha"/>. Dois motivos barram o pagamento, e cada um tem a frase dele:
///
///   * A BRIGA COM ALGUEM (`CombatState.EmBrigaComAlguem`: um golpe trocado com outro corpo nos ultimos 90 s) --
///     *"no meio de uma briga ela nao rende recursos"*.
///   * O ESMAGAMENTO (`Lenha.EsmagadoContaComoBriga` + `Esmagamento.Esmaga`): o corpo num chao mais pesado que ele
///     domina, ou com peso de treino acima do limite. O dono (2026-10-10): *"sim conta como combate, n da pra
///     mineirar se vc nem consegue se mover"*. A frase diz o motivo certo -- a gravidade, ou o peso.
///
/// NAO SE LE A TAG DE COMBATE INTEIRA, e a diferenca aparece em quem SAIU do esmagamento: neste port o dano sem
/// algoz tambem arma a tag (o esmagamento fere uma vez por segundo), e ela segue acesa 90 s depois. Quem se
/// aclimatou ou tirou o peso recebe na hora, e quem esta esmagado ouve falar de gravidade e nao de uma briga que
/// nao houve (foi o que a `--diaglenha` mediu num mundo de 2x: "no meio de uma briga", sem ninguem por perto).
/// ============================================================================================================
///
/// ============================ O TOCO QUE FICA NO LUGAR ============================
/// O dono (2026-10-10): *"sim seria bom um sprite"*. A arvore que cai deixa um TOCO onde ela estava -- na celula
/// do mapa, ou na celula em que a rebrota estava de pe -- ate a arvore voltar. E SO DESENHO: nao barra o passo (a
/// celula ja estava aberta, e e em cima dela que se constroi), e some quando a arvore rebrota ou quando alguem
/// constroi em cima (bloco, maquina, nave, rocha -- quem ve e o zelador, <see cref="ZelarPelosTocos"/>). O toco
/// some; o prazo da arvore continua -- e se o que estava em cima sair antes de ela voltar (a nave decola, a parede
/// e desmanchada), o toco VOLTA: ele esta a vista enquanto o chao dele esta livre. Viaja no `S2C.Lenha`
/// (`Toco`/`SemToco`, e no retrato de quem chega) como a celula e o TILE DA ARVORE QUE CAIU: quem tira dali o
/// desenho e o cliente (`Core/World/ArteDoToco.cs`).
/// ==================================================================================
///
/// NAO VAI PRO DISCO: o estrago de cenario tambem nao vai. Reiniciou, o mapa volta inteiro, as arvores com ele.
///
/// SO EM PLANETA (`Espaco.EhPlaneta`): e onde ha recurso (`ReservaDe`). No Mirante, no Alem e no Reino dos Deuses
/// a arvore continua sendo so cenario -- e la o `DerrubarCelula` ja se recusa a quebrar o Outro Mundo.
///
/// A MACIEIRA NAO E LENHA: e uma `Obra` (`AppleTree`), da fruta e pode ter dono. Nao paga recurso (pagar por
/// derrubar comida seria um convite; e com a semente daria pra plantar e derrubar em laco) e quebra pela regra de
/// armadura de sempre: a do mapa cai a soco e volta no reinicio, a plantada tem a forca de quem plantou.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Uma arvore caida: quando ela volta, com que tile se desenha, e o toco que ela deixou no chao.</summary>
	private sealed class Toco
	{
		public long VoltaEm;
		public TileDeArvore Tile;

		/// <summary>
		/// ONDE A ARVORE CAIU -- e onde o toco aparece. E a origem dela, menos quando quem caiu foi uma rebrota que
		/// tinha nascido noutro chao (a origem, ai, esta debaixo da parede de alguem).
		/// </summary>
		public (int X, int Y) Onde;

		/// <summary>O toco esta a vista. Falso enquanto ha algo de alguem em cima dele: o desenho sai, o prazo continua.</summary>
		public bool AVista;
	}

	/// <summary>Uma arvore que voltou a ficar de pe: de que celula do mapa ela e, e o tile dela.</summary>
	private readonly record struct Rebrota((int X, int Y) Origem, TileDeArvore Tile);

	/// <summary>A lenha de UMA zona. Tocos pela celula de ORIGEM; rebrotas e troncos golpeados pela celula em que estao.</summary>
	private sealed class LenhaDaZona
	{
		public required ZoneKey Zona { get; init; }
		public readonly Dictionary<(int X, int Y), Toco> Tocos = [];
		public readonly Dictionary<(int X, int Y), Rebrota> Rebrotas = [];

		/// <summary>Quanto do tronco cada arvore DE PE ja levou (0 a 1). So entra quem apanhou e nao caiu.</summary>
		public readonly Dictionary<(int X, int Y), double> Golpeadas = [];

		/// <summary>O mapa em que as rebrotas estao escritas. Mundo sorteado que renasce troca de mapa -- ver <see cref="AssentarLenha"/>.</summary>
		public ZoneCollision? Assentada;
	}

	/// <summary>A lenha de cada zona, pela <see cref="ZoneKey.Hash"/>. So existe pra zona em que alguma arvore ja caiu.</summary>
	private readonly Dictionary<ulong, LenhaDaZona> _lenha = [];

	/// <summary>As arvores de cada mundo sorteado VIVO, anotadas na colheita dele (<see cref="AoColherOMundo"/>).</summary>
	private readonly Dictionary<ulong, ArvoresDoMapa> _arvoresDoMundo = [];

	/// <summary>
	/// As arvores de cada planeta pre-feito, lidas do `.pedacos` na primeira pergunta. Nulo = nao deu pra ler.
	/// POR NOME DE PROPOSITO: e dado do ARQUIVO e so de planeta de arquivo, que e uma zona por nome (interior e mundo
	/// sorteado nao chegam aqui: ver <see cref="ArvoresDa"/>). O que MUDA -- o que caiu, o que rebrotou -- e por zona.
	/// </summary>
	private readonly Dictionary<string, ArvoresDoMapa?> _arvoresDoArquivo = new(StringComparer.OrdinalIgnoreCase);

	private CatalogoDeTiles? _tilesDaLenha;

	/// <summary>De quanto em quanto tempo a lenha anda, em ms. Prazo de minutos nao precisa de tique cheio.</summary>
	private const long MsEntrePassadasDaLenha = 1000;

	/// <summary>
	/// A FOLGA EM VOLTA DE ONDE SE CHEGA, em tiles: arvore que muda de lugar nao nasce a menos disto do ponto de pouso
	/// de um mundo sorteado nem do berco de um pre-feito. O pouso poe o corpo no ponto exato, sem procurar chao livre.
	/// </summary>
	private const int FolgaDaChegada = 3;

	private long _proximaLenha;
	private Queue<string>? _planetasPorLer;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--lenhateste`) -- falsos em jogo, sempre
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a arvore que cai nao vira toco -- o mundo de antes de 2026-10-10,
	/// em que cenario caido nunca voltava. Lido pela propria linha que anota o toco, em <see cref="AArvoreDoMapaCaiu"/>.
	/// </summary>
	internal bool ArvoreCaidaNaoRebrotaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a arvore volta no instante em que cai -- da pra derrubar a mesma
	/// em laco. Lido pela propria linha do prazo, em <see cref="PrazoDaRebrotaMs"/>.
	/// </summary>
	internal bool RebrotaNaHoraDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a arvore volta na origem sem olhar o que ha la -- por cima da
	/// parede, da maquina ou de quem estiver em pe. Lido pela primeira linha do <see cref="LugarPraRebrotar"/>.
	/// </summary>
	internal bool RebrotaSemOlharOLugarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): quem chega na zona recebe o retrato VAZIO -- nao ve (nem esbarra
	/// em) arvore nenhuma que rebrotou antes de ele chegar. Lido pela propria linha do <see cref="MandarLenha"/>.
	/// </summary>
	internal bool RetratoDeLenhaVazioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a rebrota fica fora do funil de queda -- so o soco a derruba, e o
	/// corpo arremessado e o chao que racha param numa arvore que nada tira do lugar. Lido pela propria linha do
	/// gancho, em <see cref="ARebrotaCaiNoLugarDaCelula"/>.
	/// </summary>
	internal bool RebrotaForaDoFunilDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): o mundo sorteado que renasce NAO recebe de volta o estrago anotado
	/// -- o estado de antes de 2026-10-10, com a parede de pe no servidor onde o cliente a abre. Lido pela propria
	/// linha do conserto, em <see cref="AoColherOMundo"/>.
	/// </summary>
	internal bool MundoRenasceSemOEstragoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a briga e lida da tag de combate INTEIRA, que o dano sem algoz
	/// tambem arma. Quem acabou de se aclimatar (ou de tirar o peso) fica 90 s sem receber, ouvindo "no meio de uma
	/// briga" -- e com a constante `Lenha.EsmagadoContaComoBriga` invertida o esmagado continua sem receber: a
	/// constante vira enfeite. Lido pela propria linha que pergunta, em <see cref="PagarALenha"/>.
	/// </summary>
	internal bool ApanharDoMundoEBrigaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): o toco e MUDO -- a arvore cai e o servidor nao conta a ninguem que
	/// ficou um toco (nem na hora, nem no retrato de quem chega). O estado de antes do pedido: so a terra batida.
	/// Lido pela propria linha que o poe no fio, em <see cref="PorOTocoAVista"/> e no <see cref="MandarLenha"/>.
	/// </summary>
	internal bool TocoMudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): o toco fica debaixo do que construiram em cima dele -- o zelador
	/// nao olha. Lido pela propria linha da pergunta, em <see cref="ZelarPelosTocos"/>.
	/// </summary>
	internal bool TocoFicaSobAObraDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): o toco que saiu da vista NAO volta quando o chao dele fica livre --
	/// a nave pousa em cima e decola, e fica so a terra batida de onde uma arvore vai brotar minutos depois. Lido
	/// pela propria linha que o devolve, em <see cref="ZelarPelosTocos"/>.
	/// </summary>
	internal bool TocoNaoVoltaDeTeste;

	/// <summary>O sorteio do pagamento, cravado pela bancada (0 a 1). Nulo em jogo: quem sorteia e o `_rng`.</summary>
	internal double? SorteioDaLenhaDeTeste;

	/// <summary>Tudo que sai pelo `S2C.Lenha` enquanto uma bancada escuta: pra quem (o id; 0 = a zona) e o fio. Nulo em jogo.</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeLenha;

	// =====================================================================
	// ONDE HA ARVORE
	// =====================================================================

	/// <summary>O indice de tiles, lido uma vez: e ele que diz que folha e de arvore e que tile a paleta do bioma pede.</summary>
	private CatalogoDeTiles TilesDaLenha()
	{
		if (_tilesDaLenha != null) return _tilesDaLenha;

		const string indice = "res://Assets/Data/tiles.json";
		_tilesDaLenha = CatalogoDeTiles.Parse(
			Godot.FileAccess.FileExists(indice) ? Godot.FileAccess.GetFileAsString(indice) : "");
		if (_tilesDaLenha.Total == 0)
			GD.PushWarning($"[lenha] sem '{indice}' -- rode o Tools/AssetPipeline. Nenhuma arvore sera reconhecida como arvore.");
		return _tilesDaLenha;
	}

	/// <summary>
	/// AS ARVORES DESTA ZONA, ou nulo onde nao ha lenha: fora de planeta, mundo sorteado que nao esta carregado, mapa
	/// sem `.pedacos`.
	/// </summary>
	private ArvoresDoMapa? ArvoresDa(ZoneKey zona)
	{
		if (!Espaco.EhPlaneta(zona)) return null;
		if (zona.Kind == ZoneKey.KindProcedural) return _arvoresDoMundo.GetValueOrDefault(zona.Hash);

		if (_arvoresDoArquivo.TryGetValue(zona.Name, out ArvoresDoMapa? lidas)) return lidas;
		return _arvoresDoArquivo[zona.Name] = LerArvoresDoArquivo(zona);
	}

	/// <summary>
	/// LE AS ARVORES DE UM PLANETA PRE-FEITO. O servidor nao desenha e nunca tinha aberto o `.pedacos`; abre agora,
	/// uma vez por planeta, so pra esta pergunta -- e solta o arquivo em seguida (fica um dicionario de centenas de
	/// entradas). Medido na Terra: 2,5 MB lidos, 253 mil celulas varridas, 327 arvores, 14 ms (com o `tiles.json`).
	/// </summary>
	private ArvoresDoMapa? LerArvoresDoArquivo(ZoneKey zona)
	{
		if (_catalogo?.Get(zona) is not { Mapa: { } mapa } e) return null;
		if (e.Pedacos.Length == 0 || !Godot.FileAccess.FileExists(e.Pedacos)
			|| PedacosDoMapa.Ler(Godot.FileAccess.GetFileAsBytes(e.Pedacos)) is not { } pedacos)
		{
			GD.PushWarning($"[lenha] {e.Zona}: sem '{e.Pedacos}' -- as arvores daqui seguem como parede comum.");
			return null;
		}

		ulong t0 = Time.GetTicksUsec();
		ArvoresDoMapa arvores = ArvoresDoMapa.DoArquivo(pedacos, TilesDaLenha(), mapa);
		GD.Print($"[lenha] {e.Zona}: {arvores.Total} arvore(s) no mapa ({(Time.GetTicksUsec() - t0) / 1000.0:0.0} ms)");
		return arvores;
	}

	private bool CenarioJaCaiu(ZoneKey zona, int cx, int cy) =>
		_cenarioCaido.TryGetValue(ChaveDeEstado(zona), out HashSet<(int X, int Y)>? caidas) && caidas.Contains((cx, cy));

	/// <summary>A ARVORE QUE O MAPA TROUXE nesta celula esta de pe? (E arvore, e parede do arquivo, e nao caiu.)</summary>
	private bool ArvoreDoMapaDePe(ZoneKey zona, ZoneCollision mapa, int cx, int cy) =>
		ArvoresDa(zona) is { } arvores && arvores.Tem(cx, cy, out _)
		&& mapa.BloqueadaNoArquivo(cx, cy) && !CenarioJaCaiu(zona, cx, cy);

	/// <summary>HA UMA ARVORE DE PE NESTA CELULA -- a do mapa, ou uma que rebrotou?</summary>
	private bool ArvoreDePe(ZoneKey zona, int cx, int cy) =>
		(_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) && z.Rebrotas.ContainsKey((cx, cy)))
		|| (MapaDaZonaOuCatalogo(zona) is { } mapa && ArvoreDoMapaDePe(zona, mapa, cx, cy));

	private LenhaDaZona LenhaDa(ZoneKey zona)
	{
		if (!_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z))
			_lenha[zona.Hash] = z = new LenhaDaZona { Zona = zona };
		return z;
	}

	// =====================================================================
	// O SOCO
	// =====================================================================

	/// <summary>
	/// O SOCO ENCONTROU UMA ARVORE? Chamado pelo <see cref="SocarCenario"/> depois da obra, da nave e do bloco, e
	/// antes da regra do turf. Devolve falso quando a celula nao e arvore de pe (o funil segue como sempre seguiu).
	///
	/// O TRONCO E ACUMULADO, e e da ARVORE e nao de quem bate: tres socos de um e o quarto de outro derrubam, e quem
	/// recebe e quem deu o ultimo -- *"quem derrubou"*. Sem sorteio: o novato conta os golpes e sabe quantos faltam.
	///
	/// O PAGAMENTO E SO DAQUI. A arvore que cai por outro caminho (o corpo arremessado, o chao que racha num golpe
	/// pesado, a rajada, a tecnica) passa pelo <see cref="DerrubarCelula"/> e vira toco igual, sem pagar ninguem.
	/// </summary>
	private bool SocarArvore(ServerPlayer a, int cx, int cy, int nivel)
	{
		if (Lenha.TroncoComoParedeDeTeste) return false;

		ZoneKey zona = a.Zone;
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa) return false;

		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z);
		bool rebrota = z != null && z.Rebrotas.ContainsKey((cx, cy));
		if (!rebrota)
		{
			if (!ArvoreDoMapaDePe(zona, mapa, cx, cy)) return false;
			// AS RECUSAS DO CENARIO CONTINUAM SENDO DO FUNIL DE SEMPRE: a beirada do mundo, o que nao se quebra e a
			// celula que tem bloco de alguem respondem la, com o aviso de la.
			if (mapa.NaBorda(cx, cy) || mapa.Indestrutivel(cx, cy) || BlocoEm(zona, cx, cy) != null) return false;
		}

		// O BAQUE SAI SEMPRE, como em toda pancada em cenario (ver `SocarCenario`).
		Baque(a, nivel);

		z ??= LenhaDa(zona);
		double parte = Lenha.ParteDoTroncoPorSoco(a.Ficha.expressedBP);
		double levado = z.Golpeadas.GetValueOrDefault((cx, cy)) + parte;
		if (!Lenha.Cedeu(levado))
		{
			z.Golpeadas[(cx, cy)] = levado;
			int faltam = (int)Math.Ceiling((1.0 - levado) / parte - 1e-9);
			Avisar(a, faltam == 1 ? "a árvore estala -- mais um soco como este." : $"a árvore estala -- mais {faltam} socos como este.");
			return true;
		}

		bool caiu = rebrota ? TirarRebrota(z, (cx, cy), viraToco: true, comPoeira: true) : DerrubarCelula(zona, cx, cy);
		if (!caiu)
		{
			// So chega aqui se o `DerrubarCelula` recusou por algo que as guardas de cima nao viram. O tronco fica no
			// limite: o proximo soco tenta de novo.
			z.Golpeadas[(cx, cy)] = levado - parte;
			Avisar(a, "a árvore não cede.");
			return true;
		}

		PagarALenha(a);
		return true;
	}

	/// <summary>A arvore acabou de cair pelo soco deste corpo: paga, ou diz por que nao paga.</summary>
	private void PagarALenha(ServerPlayer a)
	{
		// AS PROPRIAS MAOS: jogador (corpo com dono na tela), e o dono dirigindo -- a fera do Oozaru e a furia
		// lendaria socam com o corpo dele sem ele (`Cerebro`), e NPC e clone nao sao jogador (`EhJogador`).
		bool comAsProprias = EhJogador(a) && a.Cerebro == null;
		// A BRIGA COM ALGUEM, e nao a tag inteira: o dano sem algoz tambem a arma, e ela dura 90 s (ver o cabecalho).
		bool brigando = a.Combate is { } c && (ApanharDoMundoEBrigaDeTeste ? c.EmCombate > 0 : c.EmBrigaComAlguem);
		// O ESMAGADO CONTA COMO EM COMBATE (dono, 2026-10-10): a constante que ele inverte numa linha. E o estado de
		// AGORA -- a mesma pergunta que o tique do esmagamento faz --, entao quem se aclimata recebe na hora.
		bool esmagado = Lenha.EsmagadoContaComoBriga && Esmagamento.Esmaga(a.Ficha);

		if (!Lenha.Paga(comAsProprias, brigando || esmagado))
		{
			// A FRASE DIZ O MOTIVO CERTO: quem esta sozinho num planeta pesado nao pode ouvir falar de briga.
			if (comAsProprias)
				Avisar(a, brigando ? "a árvore vem abaixo -- no meio de uma briga ela não rende recursos."
					: Esmagamento.PorPeso(a.Ficha)
						? "a árvore vem abaixo -- mas o peso que você carrega te esmaga, e você não consegue aproveitar a madeira."
						: "a árvore vem abaixo -- mas a gravidade daqui te esmaga, e você não consegue aproveitar a madeira.");
			return;
		}

		int recursos = Lenha.RecursosDaQueda(SorteioDaLenhaDeTeste ?? _rng.NextDouble());
		DarRecursos(a, recursos);
		Avisar(a, $"a árvore vem abaixo: +{recursos} recursos.");
	}

	// =====================================================================
	// A QUEDA
	// =====================================================================

	/// <summary>
	/// UMA CELULA DO CENARIO ACABOU DE CAIR. Se o mapa a trouxe como arvore, ela vira toco e ganha o prazo de volta.
	/// Chamado pelo <see cref="DerrubarCelula"/>, o unico ponto por onde cenario cai -- por isso vale pro soco, pro
	/// arremesso, pro chao que racha e pra tecnica, sem que nenhum deles saiba que arvore rebrota.
	/// </summary>
	private void AArvoreDoMapaCaiu(ZoneKey zona, int cx, int cy)
	{
		if (ArvoreCaidaNaoRebrotaDeTeste) return;
		if (ArvoresDa(zona) is not { } arvores || !arvores.Tem(cx, cy, out TileDeArvore tile)) return;

		LenhaDaZona z = LenhaDa(zona);
		z.Golpeadas.Remove((cx, cy));
		var toco = new Toco { VoltaEm = NowMs() + PrazoDaRebrotaMs(), Tile = tile, Onde = (cx, cy) };
		z.Tocos[(cx, cy)] = toco;
		PorOTocoAVista(z, toco);
	}

	/// <summary>
	/// FICA UM TOCO ONDE A ARVORE CAIU, e quem esta na zona o ve agora (quem chega depois, no retrato). Vai DEPOIS do
	/// pacote da queda -- o de cenario, ou o `LenhaCaiu` da rebrota --, que e o que apaga a arvore da tela. E tambem
	/// por aqui que o toco VOLTA quando sai de cima dele o que o escondia (<see cref="ZelarPelosTocos"/>).
	/// </summary>
	private void PorOTocoAVista(LenhaDaZona z, Toco toco)
	{
		if (TocoMudoDeTeste) return;
		toco.AVista = true;

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Lenha);
		w.Put(Protocol.LenhaToco);
		PorArvoreNoFio(w, toco.Onde, toco.Tile);
		MandarPraZonaDaLenha(z.Zona, w);
	}

	/// <summary>O toco sai da vista (a arvore rebrotou, ha algo em cima dele, o admin refez o cenario). O prazo da arvore nao muda.</summary>
	private void TirarOTocoDaVista(LenhaDaZona z, Toco toco)
	{
		if (!toco.AVista) return;
		toco.AVista = false;

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Lenha);
		w.Put(Protocol.LenhaSemToco);
		w.Put((ushort)toco.Onde.X);
		w.Put((ushort)toco.Onde.Y);
		MandarPraZonaDaLenha(z.Zona, w);
	}

	/// <summary>
	/// HA UMA REBROTA NESTA CELULA? Entao e ela que cai, no lugar da celula. Chamado pelo <see cref="DerrubarCelula"/>
	/// antes de anotar o estrago: a rebrota nao e do arquivo (ela pode estar num chao que nunca foi parede, ou na
	/// origem, que JA esta no estrago -- e ai o `Add` de la diria "nao caiu" com a arvore de pe barrando o corpo).
	/// </summary>
	private bool ARebrotaCaiNoLugarDaCelula(ZoneKey zona, int cx, int cy) =>
		!RebrotaForaDoFunilDeTeste
		&& _lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) && TirarRebrota(z, (cx, cy), viraToco: true, comPoeira: true);

	/// <summary>
	/// Tira uma rebrota do mundo: da lista, do mapa e da tela de quem esta na zona.
	/// </summary>
	/// <param name="viraToco">Ela foi DERRUBADA: a arvore volta a toco, com prazo novo. Falso = ela so deixa de existir.</param>
	/// <param name="comPoeira">Quem esta na zona ve a queda (poeira e lasca). Falso = some calada.</param>
	private bool TirarRebrota(LenhaDaZona z, (int X, int Y) onde, bool viraToco, bool comPoeira)
	{
		if (!z.Rebrotas.Remove(onde, out Rebrota r)) return false;

		z.Golpeadas.Remove(onde);
		MapaDaZonaOuCatalogo(z.Zona)?.Tombar(onde.X, onde.Y);

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Lenha);
		w.Put(Protocol.LenhaCaiu);
		w.Put((ushort)onde.X);
		w.Put((ushort)onde.Y);
		w.Put(comPoeira);
		MandarPraZonaDaLenha(z.Zona, w);

		if (viraToco)
		{
			// O PRAZO E DA ORIGEM (e pra la que a arvore tenta voltar); O TOCO FICA ONDE ELA CAIU.
			var toco = new Toco { VoltaEm = NowMs() + PrazoDaRebrotaMs(), Tile = r.Tile, Onde = onde };
			z.Tocos[r.Origem] = toco;
			PorOTocoAVista(z, toco);
		}
		return true;
	}

	private long PrazoDaRebrotaMs() =>
		RebrotaNaHoraDeTeste ? 0 : (long)(Lenha.SegundosAteRebrotar(_rng.NextDouble()) * 1000);

	// =====================================================================
	// A VOLTA
	// =====================================================================

	/// <summary>
	/// A LENHA ANDA: uma passada por segundo. Um relogio so pro servidor inteiro, como o das macas
	/// (`TickDasArvores`) -- e so as zonas em que alguma arvore ja caiu entram na conta.
	/// </summary>
	private void TickDaLenha()
	{
		long agora = NowMs();
		if (agora < _proximaLenha) return;
		_proximaLenha = agora + MsEntrePassadasDaLenha;
		PassarALenha(agora);
	}

	private void PassarALenha(long agora)
	{
		LerUmPlanetaPorPassada();

		foreach (LenhaDaZona z in _lenha.Values)
		{
			// MUNDO SORTEADO DESCARREGADO: sem mapa nao ha como saber se o lugar esta livre. Os tocos esperam; quando
			// alguem pousar la de novo, a colheita do mundo os passa antes do pouso (`AoColherOMundo`).
			if (MapaDaZonaOuCatalogo(z.Zona) is not { } mapa) continue;
			if (ZonaMorta(z.Zona)) continue;   // planeta destruido nao rebrota

			AssentarLenha(z, mapa);
			ZelarPelaLenha(z);
			ZelarPelosTocos(z);
			RebrotarOQueVenceu(z, mapa, agora);
		}

		// O PLANO DE ARVORES DE MUNDO DESCARREGADO SAI COM ELE (`PodarZonasVazias`): ate 250 KB por mundo de 1000.
		if (_arvoresDoMundo.Count > _zonasGeradas.Count)
			foreach (ulong hash in _arvoresDoMundo.Keys.Where(h => !_zonasGeradas.ContainsKey(h)).ToList())
				_arvoresDoMundo.Remove(hash);
	}

	/// <summary>
	/// OS PLANETAS PRE-FEITOS SAO LIDOS AOS POUCOS, um por passada, nos primeiros segundos do servidor: sao ~5 ms por
	/// planeta, e deixar pra primeira arvore que cai seria pagar isso no quadro de um golpe. Quem perguntar antes
	/// (a bancada, um soco no primeiro segundo) le na hora -- ver <see cref="ArvoresDa"/>.
	/// </summary>
	private void LerUmPlanetaPorPassada()
	{
		_planetasPorLer ??= new Queue<string>(Espaco.PreFeitos().Select(p => p.Nome));
		if (_planetasPorLer.TryDequeue(out string? nome)) ArvoresDa(ZoneKey.Premade(nome));
	}

	/// <summary>
	/// AS REBROTAS DESTA ZONA, ESCRITAS NO MAPA DELA -- se ainda nao estao. O mapa de um planeta pre-feito e sempre o
	/// mesmo objeto; o de um mundo sorteado e descartado quando o mundo esvazia e nasce outro na volta. E o mesmo
	/// molde do `AssentarBase` dos blocos.
	/// </summary>
	private static void AssentarLenha(LenhaDaZona z, ZoneCollision mapa)
	{
		if (ReferenceEquals(z.Assentada, mapa)) return;
		foreach ((int x, int y) in z.Rebrotas.Keys) mapa.Rebrotar(x, y);
		z.Assentada = mapa;
	}

	/// <summary>
	/// A ORIGEM DEIXOU DE ESTAR CAIDA? Entao a arvore do mapa esta de pe de novo (um admin refez o cenario da zona),
	/// e o que este arquivo guardava dela acabou: o toco some, a rebrota sai. Olhar o estrago aqui -- em vez de
	/// pendurar um aviso em cada lugar que o esvazia -- e o que cobre o verb de admin e o proximo que aparecer.
	/// </summary>
	private void ZelarPelaLenha(LenhaDaZona z)
	{
		if (z.Tocos.Count == 0 && z.Rebrotas.Count == 0) return;

		_cenarioCaido.TryGetValue(ChaveDeEstado(z.Zona), out HashSet<(int X, int Y)>? caidas);
		bool DePeNoMapa((int X, int Y) origem) => caidas == null || !caidas.Contains(origem);

		// SEM ALOCAR NO CAMINHO COMUM: a passada roda todo segundo, e quase nunca ha o que tirar.
		List<(int X, int Y)>? tocosDeMais = null, rebrotasDeMais = null;
		foreach ((int X, int Y) origem in z.Tocos.Keys)
			if (DePeNoMapa(origem)) (tocosDeMais ??= []).Add(origem);
		foreach (((int X, int Y) onde, Rebrota r) in z.Rebrotas)
			if (DePeNoMapa(r.Origem)) (rebrotasDeMais ??= []).Add(onde);

		if (tocosDeMais != null)
			foreach ((int X, int Y) origem in tocosDeMais)
				if (z.Tocos.Remove(origem, out Toco? toco)) TirarOTocoDaVista(z, toco);
		if (rebrotasDeMais != null)
			foreach ((int X, int Y) onde in rebrotasDeMais) TirarRebrota(z, onde, viraToco: false, comPoeira: false);
	}

	/// <summary>
	/// O TOCO ESTA A VISTA ENQUANTO O CHAO DELE ESTA LIVRE. Construiram em cima -- bloco de base (parede, piso,
	/// porta), maquina, nave parada ou rocha de minerio na celula --, ele sai da vista. So o desenho sai: a arvore
	/// continua com o prazo dela, e na hora de voltar o <see cref="LugarPraRebrotar"/> ve o lugar tomado e procura
	/// outro chao.
	///
	/// E SE O QUE ESTAVA EM CIMA SAIR ANTES DISSO, O TOCO VOLTA. Nave pousa e decola, rocha e colhida, parede e
	/// desmanchada: a arvore continua caida ali e vai rebrotar ali mesmo. Sem a volta, quem estacionasse a nave num
	/// toco o apagava de vez, e minutos depois uma arvore brotava de um chao que nao mostrava nada.
	///
	/// OLHADO AQUI, uma vez por passada, pelo mesmo motivo do <see cref="ZelarPelaLenha"/>: quem ergue parede, assenta
	/// maquina ou pousa nave nao precisa saber que existe toco, e o proximo jeito de ocupar chao ja nasce coberto --
	/// na ida e na volta. O preco e ate um segundo de atraso em cada uma.
	/// </summary>
	private void ZelarPelosTocos(LenhaDaZona z)
	{
		if (TocoFicaSobAObraDeTeste) return;
		foreach (Toco toco in z.Tocos.Values)
		{
			bool tomado = EstaNoChaoDeAlguem(z.Zona, toco.Onde.X, toco.Onde.Y);
			if (toco.AVista && tomado) TirarOTocoDaVista(z, toco);
			else if (!toco.AVista && !tomado && !TocoNaoVoltaDeTeste) PorOTocoAVista(z, toco);
		}
	}

	/// <summary>Os tocos cujo prazo venceu voltam -- ate <see cref="Lenha.RebrotasPorPassada"/> de cada vez, os mais antigos primeiro.</summary>
	private void RebrotarOQueVenceu(LenhaDaZona z, ZoneCollision mapa, long agora)
	{
		// SEM ALOCAR ENQUANTO NINGUEM VENCEU: a passada roda todo segundo, e o prazo e de minutos.
		List<KeyValuePair<(int X, int Y), Toco>>? vencidos = null;
		foreach (KeyValuePair<(int X, int Y), Toco> t in z.Tocos)
			if (t.Value.VoltaEm <= agora) (vencidos ??= []).Add(t);
		if (vencidos == null) return;

		foreach (((int X, int Y) origem, Toco toco) in vencidos.OrderBy(t => t.Value.VoltaEm).Take(Lenha.RebrotasPorPassada))
		{
			if (LugarPraRebrotar(z, mapa, origem) is not { } onde)
			{
				// SEM CHAO AGORA (alguem em cima do toco, ou tudo construido em volta): tenta de novo daqui a pouco.
				toco.VoltaEm = agora + (long)(Lenha.NovaTentativaS * 1000);
				continue;
			}

			z.Tocos.Remove(origem);
			TirarOTocoDaVista(z, toco);   // antes da arvore: o toco sai, e ela entra no lugar (ou noutro chao)
			z.Rebrotas[onde] = new Rebrota(origem, toco.Tile);
			mapa.Rebrotar(onde.X, onde.Y);
			z.Assentada = mapa;

			NetDataWriter w = Protocol.Begin(Protocol.S2C.Lenha);
			w.Put(Protocol.LenhaBrotou);
			PorArvoreNoFio(w, onde, toco.Tile);
			MandarPraZonaDaLenha(z.Zona, w);
		}
	}

	/// <summary>
	/// ONDE ESTA ARVORE VOLTA. No lugar dela, se ele esta livre. Se alguem esta em pe em cima do toco, espera (nulo):
	/// gente passa. Se o lugar foi TOMADO -- parede, piso ou porta de alguem, maquina, nave --, o chao livre mais
	/// perto, anel por anel, ate <see cref="Lenha.RaioDeReplantio"/> tiles; sem nenhum, espera.
	/// </summary>
	private (int X, int Y)? LugarPraRebrotar(LenhaDaZona z, ZoneCollision mapa, (int X, int Y) origem)
	{
		if (RebrotaSemOlharOLugarDeTeste) return origem;

		if (!OLugarFoiTomado(z.Zona, mapa, origem.X, origem.Y))
			return AlguemPisaEm(z.Zona, origem.X, origem.Y) ? null : origem;

		for (int r = 1; r <= Lenha.RaioDeReplantio; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca do anel
					if (ServeDeChaoNovoPraArvore(z.Zona, mapa, origem.X + dx, origem.Y + dy))
						return (origem.X + dx, origem.Y + dy);
				}
		return null;
	}

	/// <summary>
	/// O LUGAR DE ORIGEM ESTA OCUPADO POR ALGO QUE FICA? O que barra a celula agora (parede ou porta erguida, rocha
	/// de minerio) e o que esta nela sem barrar (piso erguido, maquina, nave parada). Corpo nao conta: corpo sai.
	///
	/// AS PERGUNTAS VAO AS LISTAS, e nao so ao mapa: o mapa de um mundo sorteado que acabou de renascer ainda nao tem
	/// as camadas de base e de rocha escritas (quem as escreve e a chegada do primeiro corpo), e os tocos vencidos
	/// rebrotam na colheita, antes disso.
	/// </summary>
	private bool OLugarFoiTomado(ZoneKey zona, ZoneCollision mapa, int cx, int cy) =>
		mapa.BlockedCell(cx, cy) || EstaNoChaoDeAlguem(zona, cx, cy);

	/// <summary>Ha nesta celula um bloco de base, uma maquina, uma nave parada ou uma rocha de minerio?</summary>
	private bool EstaNoChaoDeAlguem(ZoneKey zona, int cx, int cy) =>
		BlocoEm(zona, cx, cy) != null || ObraNaCelula(zona, cx, cy) != null
		|| NaveNaCelula(zona, cx, cy) != null || RochaNaCelula(zona, cx, cy);

	/// <summary>
	/// ESTE CHAO RECEBE UMA ARVORE QUE NUNCA ESTEVE NELE? Chao de verdade (`ServeDeChao`: sem parede, beirada, agua
	/// nem nuvem), que o arquivo nunca marcou como parede (nada de nascer em ruina), a ceu aberto, sem nada de
	/// ninguem e sem ninguem em cima -- e numa CLAREIRA: as oito celulas em volta tambem sao chao. E a clareira que
	/// garante que arvore nova nunca fecha uma passagem de um tile (uma porta, uma ponte, a boca de uma caverna).
	/// </summary>
	private bool ServeDeChaoNovoPraArvore(ZoneKey zona, ZoneCollision mapa, int cx, int cy)
	{
		if (!mapa.ServeDeChao(cx, cy) || mapa.BloqueadaNoArquivo(cx, cy)
			|| mapa.NasceuDentro(cx, cy) || mapa.Coberta(cx, cy)) return false;

		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if ((dx != 0 || dy != 0) && !mapa.ServeDeChao(cx + dx, cy + dy)) return false;

		if (PertoDeOndeSeChega(zona, cx, cy)) return false;
		if (EstaNoChaoDeAlguem(zona, cx, cy)) return false;

		// O CANTEIRO DO POMAR FICA LIVRE, com a macieira de pe ou nao (a que cai volta a nascer na mesma celula --
		// `GameServer.Pomar.cs`): a arvore que muda de lugar nao toma o lugar da fruta, nem encosta nele.
		if (_pomares.TryGetValue(zona.Hash, out PomarDaZona? pomar))
			foreach ((int px, int py) in pomar.Celulas)
				if (Math.Max(Math.Abs(px - cx), Math.Abs(py - cy)) <= 1) return false;

		return !AlguemPisaEm(zona, cx, cy);
	}

	/// <summary>A celula esta a menos de <see cref="FolgaDaChegada"/> tiles do ponto de pouso (mundo sorteado) ou do berco (pre-feito)?</summary>
	private bool PertoDeOndeSeChega(ZoneKey zona, int cx, int cy)
	{
		int ox, oy;
		if (ChegadaDaZonaGerada(zona) is { } pouso)
		{
			ox = (int)MathF.Floor(pouso.X / ZoneCollision.TileSize);
			oy = (int)MathF.Floor(pouso.Y / ZoneCollision.TileSize);
		}
		else if (zona.Kind == ZoneKey.KindPremade && _catalogo?.Get(zona)?.Berco is { } berco) (ox, oy) = (berco.Cx, berco.Cy);
		else return false;

		return Math.Max(Math.Abs(cx - ox), Math.Abs(cy - oy)) <= FolgaDaChegada;
	}

	/// <summary>
	/// ALGUM CORPO TEM OS PES NESTA CELULA? Pela caixa dos pes (`CelulasDaCaixa`), que e o que o `MoveRules` usa pra
	/// parar o corpo: arvore que nascesse onde uma quina dela encosta deixaria o corpo dentro de parede. Vale pra
	/// todo corpo da zona -- jogador, NPC, caido.
	/// </summary>
	private bool AlguemPisaEm(ZoneKey zona, int cx, int cy)
	{
		foreach (ServerPlayer p in ZoneList(zona.Hash))
		{
			(int x0, int y0, int x1, int y1) = CelulasDaCaixa(p.Pos);
			if (cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1) return true;
		}
		return false;
	}

	// =====================================================================
	// O MUNDO SORTEADO QUE NASCE (E O QUE RENASCE)
	// =====================================================================

	/// <summary>
	/// UM MUNDO SORTEADO ACABOU DE SER COLHIDO (`TickDasGeracoes`), antes de alguem pousar nele.
	///
	/// 1. AS ARVORES DELE SAO ANOTADAS AQUI: e o unico instante em que o servidor tem a cobertura na mao (ele so
	///    guarda a colisao -- ver `ZonaGerada`).
	///
	/// 2. O ESTRAGO QUE JA ESTAVA ANOTADO VOLTA A VALER. Um mundo vazio e descartado quando o cache passa do teto
	///    (`PodarZonasVazias`) e nasce de novo, da seed, quando alguem volta -- com TODA parede de pe na colisao nova,
	///    enquanto o `_cenarioCaido` (que e por chave de estado -- `ChaveDeEstado` -- e nao foi descartado) continua mandando o cliente abrir as
	///    celulas caidas. O corpo andava na tela por onde o servidor via parede. Antes da lenha era raro (so com oito
	///    mundos vivos); com arvore caindo a toda hora em planeta sorteado, deixaria um toco que nunca mais abre.
	///
	/// 3. AS REBROTAS VOLTAM PRO MAPA NOVO, e os tocos que venceram enquanto o mundo estava descarregado rebrotam ja
	///    -- quem pousa recebe o retrato pronto, em vez de ver a floresta aparecer em volta dele.
	/// </summary>
	private void AoColherOMundo(MundoProcedural ficha, TerrenoGerado t)
	{
		ZoneKey zona = ZoneKey.Procedural(ficha.Nome, ficha.Seed);
		_arvoresDoMundo[zona.Hash] = ArvoresDoMapa.DoTerreno(t, TilesDaLenha());

		if (!MundoRenasceSemOEstragoDeTeste && _cenarioCaido.TryGetValue(ChaveDeEstado(zona), out HashSet<(int X, int Y)>? caidas))
			foreach ((int cx, int cy) in caidas) AbrirACelulaCaida(zona, cx, cy);

		if (!_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z)) return;
		AssentarLenha(z, t.Colisao);
		ZelarPelaLenha(z);
		RebrotarOQueVenceu(z, t.Colisao, NowMs());
	}

	// =====================================================================
	// REDE
	// =====================================================================

	/// <summary>Uma arvore no fio (a que rebrotou, ou a que caiu e deixou toco): a celula e o tile dela.</summary>
	private static void PorArvoreNoFio(NetDataWriter w, (int X, int Y) onde, TileDeArvore tile)
	{
		w.Put((ushort)onde.X);
		w.Put((ushort)onde.Y);
		w.Put(tile.Fonte);
		w.Put(tile.Ax);
		w.Put(tile.Ay);
	}

	private void MandarPraZonaDaLenha(ZoneKey zona, NetDataWriter w)
	{
		EscutaDeLenha?.Add((0, w.CopyData()));
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O RETRATO DA LENHA DA ZONA, pra quem chega nela -- ao lado do retrato do estrago (`MandarCenario`), e pelo
	/// mesmo motivo: a arvore que voltou e o toco da que caiu sao estado do mapa, e quem chega depois tem de ver (e
	/// esbarrar em) o mesmo que quem estava. As rebrotas primeiro, os tocos a vista depois. SAI SEMPRE, mesmo vazio,
	/// e com a zona dentro: e o que diz ao cliente de que chao e a lista -- inclusive que a do planeta de onde ele
	/// veio nao vale mais.
	/// </summary>
	private void MandarLenha(ServerPlayer pl)
	{
		LenhaDaZona? z = RetratoDeLenhaVazioDeTeste ? null : _lenha.GetValueOrDefault(pl.Zone.Hash);

		// O MAPA DE UM MUNDO QUE RENASCEU e outro objeto: as rebrotas entram nele antes de o corpo dar o primeiro passo.
		if (z != null && MapaDaZonaOuCatalogo(z.Zona) is { } mapa) AssentarLenha(z, mapa);

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Lenha);
		w.Put(Protocol.LenhaRetrato);
		w.Put(pl.Zone.Hash);
		w.Put(z?.Rebrotas.Count ?? 0);
		if (z != null)
			foreach (((int X, int Y) onde, Rebrota r) in z.Rebrotas) PorArvoreNoFio(w, onde, r.Tile);

		w.Put(z?.Tocos.Values.Count(t => t.AVista) ?? 0);
		if (z != null)
			foreach (Toco toco in z.Tocos.Values)
				if (toco.AVista) PorArvoreNoFio(w, toco.Onde, toco.Tile);

		EscutaDeLenha?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--lenhateste` e o robo `--diaglenha`, no modo host)
	// =====================================================================

	/// <summary>Quantos tocos e quantas rebrotas ha nesta zona agora.</summary>
	internal (int Tocos, int Rebrotas) LenhaDeTeste(ZoneKey zona) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) ? (z.Tocos.Count, z.Rebrotas.Count) : (0, 0);

	/// <summary>A rebrota desta celula: de que origem ela e e o tile dela. Nulo = nao ha rebrota aqui.</summary>
	internal ((int X, int Y) Origem, TileDeArvore Tile)? RebrotaDeTeste(ZoneKey zona, int cx, int cy) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) && z.Rebrotas.TryGetValue((cx, cy), out Rebrota r) ? (r.Origem, r.Tile) : null;

	/// <summary>Onde esta de pe a rebrota da arvore desta ORIGEM. Nulo = ela nao rebrotou (e toco, ou esta de pe no mapa).</summary>
	internal (int X, int Y)? RebrotaDaOrigemDeTeste(ZoneKey zona, int ox, int oy)
	{
		if (!_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z)) return null;
		foreach (((int X, int Y) onde, Rebrota r) in z.Rebrotas)
			if (r.Origem == (ox, oy)) return onde;
		return null;
	}

	/// <summary>Todas as rebrotas da zona: a celula e o tile de cada uma, como vao no retrato.</summary>
	internal List<(int X, int Y, TileDeArvore Tile)> RebrotasDeTeste(ZoneKey zona) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) ? [.. z.Rebrotas.Select(r => (r.Key.X, r.Key.Y, r.Value.Tile))] : [];

	/// <summary>Ha um toco esperando nesta origem? (A arvore caida, com prazo -- esteja o toco a vista ou nao.)</summary>
	internal bool TocoDeTeste(ZoneKey zona, int cx, int cy) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) && z.Tocos.ContainsKey((cx, cy));

	/// <summary>Ha um toco A VISTA nesta celula -- a celula em que a arvore caiu? E o que quem esta na zona ve desenhado.</summary>
	internal bool TocoAVistaDeTeste(ZoneKey zona, int cx, int cy) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z) && z.Tocos.Values.Any(t => t.AVista && t.Onde == (cx, cy));

	/// <summary>Os tocos a vista da zona: a celula de cada um e o tile da arvore que caiu, como vao no retrato.</summary>
	internal List<(int X, int Y, TileDeArvore Tile)> TocosAVistaDeTeste(ZoneKey zona) =>
		_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z)
			? [.. z.Tocos.Values.Where(t => t.AVista).Select(t => (t.Onde.X, t.Onde.Y, t.Tile))] : [];

	/// <summary>Ha uma arvore de pe nesta celula (a do mapa, ou uma rebrota)? A mesma pergunta do soco.</summary>
	internal bool ArvoreDePeDeTeste(ZoneKey zona, int cx, int cy) => ArvoreDePe(zona, cx, cy);

	/// <summary>O tile de uma arvore pelo nome da folha e do estado, resolvido pelo indice de tiles. Nulo = o indice nao tem.</summary>
	internal TileDeArvore? TileDeArvoreDeTeste(string atlas, string estado) =>
		TilesDaLenha().Achar(atlas, estado) is { } q ? new TileDeArvore((ushort)q.Fonte, (ushort)q.X, (ushort)q.Y) : null;

	/// <summary>
	/// A ARVORE DO MAPA DE PE MAIS PERTO DE UM PONTO que da pra socar do sul e que esta longe da beirada. A celula de
	/// baixo tem de ser chao que NUNCA foi parede: assim quem soca daqui nao fica em pe em cima do toco de outra
	/// arvore (e ninguem em cima de um toco e o que a bancada precisa pra medir a volta). Devolve a celula e o tile.
	/// </summary>
	/// <param name="soDesteTile">So arvore desenhada com este tile (a de folha de turf, por exemplo). Nulo = qualquer uma.</param>
	internal (int X, int Y, TileDeArvore Tile)? ArvoreSocavelPertoDeTeste(ZoneKey zona, Vec2 perto, HashSet<(int X, int Y)>? menos = null,
																		  TileDeArvore? soDesteTile = null)
	{
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa || ArvoresDa(zona) is not { } arvores) return null;
		(int px, int py) = CelulaDoPonto(perto);

		(int X, int Y, TileDeArvore Tile)? melhor = null;
		long menor = long.MaxValue;
		foreach ((int x, int y) in arvores.Todas())
		{
			if (menos?.Contains((x, y)) == true) continue;
			if (!ArvoreDoMapaDePe(zona, mapa, x, y) || mapa.NaBorda(x, y, 4) || mapa.Indestrutivel(x, y)) continue;
			if (!mapa.ServeDeChao(x, y + 1) || mapa.BloqueadaNoArquivo(x, y + 1)) continue;
			if (ObraNaCelula(zona, x, y) != null || BlocoEm(zona, x, y) != null) continue;
			long d = (long)(x - px) * (x - px) + (long)(y - py) * (y - py);
			if (d >= menor) continue;
			arvores.Tem(x, y, out TileDeArvore tile);
			if (soDesteTile is { } pedido && tile != pedido) continue;
			menor = d;
			melhor = (x, y, tile);
		}
		return melhor;
	}

	/// <summary>Vence o prazo de todos os tocos da zona e roda a passada de producao: o que tiver chao rebrota agora.</summary>
	internal void VencerALenhaDeTeste(ZoneKey zona)
	{
		if (_lenha.TryGetValue(zona.Hash, out LenhaDaZona? z))
			foreach (Toco t in z.Tocos.Values) t.VoltaEm = 0;
		PassarALenha(NowMs());
	}

	/// <summary>
	/// PALCO DO ROBO: poe o jogador um tile ao sul da celula, olhando pra ela, com a tag de combate zerada -- pelo
	/// teleporte de servidor (`CravarPosicao`) e pelo olhar cravado (`CravarOlhar`), que e o que a tela dele obedece.
	/// </summary>
	internal bool AoPeDaArvoreDeTeste(int idDoJogador, int cx, int cy)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return false;
		const int T = ZoneCollision.TileSize;
		CravarPosicao(pl, new Vec2(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY));
		CravarOlhar(pl, Facing.North);
		pl.Combate.EmCombate = 0;
		pl.Combate.BrigandoComAlguem = 0;   // um bicho bravo pode ter mordido o robo no caminho
		return true;
	}

	/// <summary>O saldo de recursos de um jogador, como o servidor o tem. -1 = nao esta no mundo.</summary>
	internal double RecursosDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl) ? pl.Ficha.Recursos : -1;

	/// <summary>
	/// PALCO DO ROBO: crava quanta gravidade o corpo do jogador DOMINA (a maestria -- acima dela ele e esmagado).
	/// E como o robo se aclimata a um planeta pesado sem treinar meia hora, e como volta a ser novato. Devolve a
	/// maestria de antes; -1 = o jogador nao esta no mundo.
	/// </summary>
	internal double DominarGravidadeDeTeste(int idDoJogador, double maestria)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return -1;
		double antes = pl.Ficha.GravMastered;
		pl.Ficha.GravMastered = maestria;
		return antes;
	}

	/// <summary>A gravidade que o corpo do jogador sente agora (planeta + maquina), e se ela o esmaga. (-1, falso) = nao esta no mundo.</summary>
	internal (double Gravidade, bool Esmagado) GravidadeDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl)
			? (Esmagamento.Gravidade(pl.Ficha), Esmagamento.Esmaga(pl.Ficha)) : (-1, false);

	/// <summary>PALCO DO ROBO: leva o jogador a outro planeta do mapa (ou de volta), pelo `MoveToZone` de producao.</summary>
	internal void ViajarDeTeste(int idDoJogador, ZoneKey destino, Vec2? onde = null) =>
		MoveToZone(idDoJogador, destino, onde ?? PontoDeNascimento(destino));

	/// <summary>
	/// PALCO DO ROBO: assenta uma maquina do mapa (sem dono, fora do disco) nesta celula, refaz a colisao das obras
	/// da zona e avisa quem esta nela -- os tres passos de quem instala de verdade (`GameServer.Tech.cs`). E o
	/// "lugar tomado" que faz a arvore nascer noutro chao. Devolve o id dela, pra <see cref="TirarMaquinaDeTeste"/>.
	/// </summary>
	internal int OcuparComMaquinaDeTeste(ZoneKey zona, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		var maquina = new Obra
		{
			Id = _proximaObraId++, Tipo = "Research_Station", DoMapa = true, Aparafusada = true,
			X = cx * T + T / 2f, Y = cy * T + T / 2f - MoveRules.FeetOffsetY,
		};
		maquina.PorZona(zona);
		_noChao.Add(maquina);
		AplicarColisaoDasObras(zona);
		MandarObras(zona);
		return maquina.Id;
	}

	/// <summary>Tira a maquina que o <see cref="OcuparComMaquinaDeTeste"/> pos.</summary>
	internal void TirarMaquinaDeTeste(int id)
	{
		if (_noChao.FirstOrDefault(o => o.Id == id) is not { } maquina) return;
		_noChao.Remove(maquina);
		AplicarColisaoDasObras(maquina.Zona);
		MandarObras(maquina.Zona);
	}

	/// <summary>
	/// Esquece a lenha de uma zona e devolve ao mapa as arvores que a bancada derrubou nela: as rebrotas saem, os
	/// tocos somem, e as celulas de origem saem do estrago e voltam a barrar. So a bancada chama.
	/// </summary>
	internal void DesfazerALenhaDeTeste(ZoneKey zona)
	{
		if (!_lenha.Remove(zona.Hash, out LenhaDaZona? z)) return;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona), vista = MapaDaVista(zona);
		_cenarioCaido.TryGetValue(ChaveDeEstado(zona), out HashSet<(int X, int Y)>? caidas);

		foreach ((int X, int Y) onde in z.Rebrotas.Keys) mapa?.Tombar(onde.X, onde.Y);
		foreach ((int X, int Y) origem in z.Tocos.Keys.Concat(z.Rebrotas.Values.Select(r => r.Origem)))
		{
			caidas?.Remove(origem);
			mapa?.Fechar(origem.X, origem.Y);
			vista?.Fechar(origem.X, origem.Y);
		}
	}
}
