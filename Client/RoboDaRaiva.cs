using System.Collections;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA RAIVA POR AMIGO (`--diagraiva`) -- a ponta do CLIENTE do pedido do dono de 2026-10-11: *"no dm tinha
/// uma cinematica q tocava a musica do gohan anger e n cheguei a testar aqui, verifique pra mim"*.
///
/// A regra (quem se enfurece, em que degrau, quanto vale, o salto de Ki, a subida pelas formas, a meditacao) e
/// medida pela bancada do servidor (`--raivateste`). O que ela nao ve e a TELA e o SOM de quem joga:
///
///   * o pacote da furia CHEGA, e a cinematica nasce em cima do corpo (ondas de choque, pedra solta, cratera) sem
///     prender ninguem -- da pra andar no meio dela;
///   * a cena NAO TEM VERMELHO (dono, 2026-10-11): a chama da cena nao acende, os aneis saem na cor do ki do
///     proprio corpo, e a aura que se ve e a NORMAL -- a que o Ki acima do tanque acende sozinha, sem tecla C.
///     Quem nao tem controle de ki nao salta, e nao ganha aura nenhuma;
///   * o TEMA entra na camada da raiva, toca alem da cena, e ha UM tocador no ar -- e sentar pra meditar acaba com
///     a raiva sem calar o tema nem devolver o Ki;
///   * o amigo que so CAI nao acende nada disso;
///   * com o requisito de uma forma NOVA cumprido, a furia TRANSFORMA SOZINHA (ninguem aperta C): a cinematica e o
///     tema sao os da transformacao, os da raiva nao tocam, e o tema da raiva que ainda estivesse no ar e cortado
///     -- quando o da forma acaba, ele nao volta;
///   * com a forma JA despertada e nada novo a estrear, a cena e a da RAIVA e o corpo fica como esta;
///   * na base, com os requisitos de uma forma que fica um degrau acima, chegam DOIS pacotes de forma -- o degrau
///     que ja era dele, sem cena, e a estreia -- e a cinematica no corpo e a da forma nova;
///   * a raiva COMUM (o amigo so cai) tambem transforma sozinha, mas cobra mais poder (dono, 2026-10-11): entre
///     os dois precos nada muda; com o preco pago chega a estreia -- sem pacote de furia, sem tema da raiva e sem
///     o Ki no teto.
///
/// O palco e do servidor (`GameServer.PalcoDaRaivaDeTeste`): um amigo declarado e um algoz ao lado do host. Dai
/// pra frente e o jogo: o funil da derrota, o gancho da raiva, o fio, o `World.AoIrromperFuria`, o `AudioDirector`.
///
/// Rode com janela (no segundo monitor), no modo host, em campo aberto, com um Saiyajin (os passos de
/// transformacao sao o Super Saiyajin e o 2, que a propria furia abre):
///   --host --rede 8321 --campoteste 8 --horateste 0.5 --diagraiva --raca Saiyan --conta bancada_draiva_ag --nome Gohan
/// A LUZ DO PALCO quem trava e o proprio robo (o `--horateste 0.5` crava o meio-dia da TERRA, e um Saiyajin nasce
/// em Vegeta): ver <see cref="TravarALuzDoPalco"/>. As fotos saem em `user://raiva-0N-*.png`.
/// </summary>
public partial class RoboDaRaiva : Node
{
	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou, _assinou;

	/// <summary>Os pacotes de furia que CHEGARAM a este cliente (o id de quem irrompeu), na ordem.</summary>
	private readonly List<int> _furias = [];

	/// <summary>Os pacotes de FORMA do dono que chegaram a este cliente (de onde, pra onde, com que degrau de cena), na ordem.</summary>
	private readonly List<(string De, string Para, DegrauDeCena Degrau)> _formas = [];

	/// <summary>O que a ultima <see cref="MedirOVermelhoDaCena"/> achou: o pico de pixels vermelhos em volta do corpo.</summary>
	private int _vermelhosNoPico;

	/// <summary>
	/// Onde a ultima <see cref="PixelsEmVoltaDeMim"/> pos a caixa -- vai no detalhe da montagem, pra o log dizer se
	/// a regua da tela estava em cima do corpo (uma caixa fora do lugar mede grama e fica verde por ausencia).
	/// </summary>
	private string _ondeLi = "";

	/// <summary>
	/// O MAIS ALTO que o Ki do dono chegou NA FICHA DESTE CLIENTE desde o ultimo `_picoDoKi = 0`, em razao do tanque
	/// (1,4 = 140%). Anotado a cada quadro, inclusive nos de espera do roteiro.
	///
	/// O SALTO DA FURIA SE MEDE POR ELE, E NAO SO PELA BARRA DE AGORA. Desde 2026-10-13 o Ki acima do tanque ESCORRE
	/// (item 33 do pedido da v0.2.1 -- `CargaDeKi.VazamentoPorSegundo`: 2,5% do tanque por segundo a 140%), entao a
	/// barra lida um segundo depois do salto ja esta tres pontos abaixo do teto. A checagem antiga ("a barra de agora
	/// esta a tres pontos do teto") passou a depender da DEMORA da leitura: passava raspando numa linha e reprovava na
	/// seguinte, com a regra certa nos dois casos. Ver <see cref="OKiSaltouProTeto"/>.
	/// </summary>
	private double _picoDoKi;

	/// <summary>
	/// Quanto a barra pode ter perdido entre o salto e a leitura sem que isso seja "o salto se desfez": uns tres
	/// segundos de vazamento a 140% (`0,5% + 5% x 0,4` = 2,5% do tanque por segundo).
	/// </summary>
	private const double FolgaDoVazamento = 0.08;

	/// <summary>
	/// O KI SALTOU PRO TETO DE CARGA -- E CONTINUA LA EM CIMA? Duas perguntas:
	///   * o PICO desde o ultimo `_picoDoKi = 0` encostou no teto: o salto aconteceu, e foi pro teto (nem mais, nem menos);
	///   * a barra de AGORA ainda esta perto dele: so perdeu o que o vazamento explica, e nao o salto inteiro (que e o
	///     que "atravessou a transformacao" quer dizer nos passos 3 e 5).
	/// </summary>
	private bool OKiSaltouProTeto(GameClient cli) =>
		Math.Abs(_picoDoKi - cli.Sheet.TetoKi) < 0.03 && RazaoDeKiNaFicha(cli) > cli.Sheet.TetoKi - FolgaDoVazamento;

	private string OKiEmTexto(GameClient cli) =>
		$"pico {_picoDoKi * 100:0}%, agora {RazaoDeKiNaFicha(cli) * 100:0}% do tanque, teto {cli.Sheet.TetoKi * 100:0}%";

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[draiva]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[draiva]   FALHA {nome}   [{detalhe}]"); }
	}

	private void AoIrromper(int id) => _furias.Add(id);

	private void AoMudarForma(int quem, int de, int para, DegrauDeCena degrau, bool dominada)
	{
		if (quem != GameClient.Instance?.LocalId) return;
		_formas.Add((Catalogo.PorRede((ushort)de)?.Id ?? "?", Catalogo.PorRede((ushort)para)?.Id ?? "?", degrau));
	}

	public override void _ExitTree()
	{
		// os dois defeitos injetados e a trava da luz sao estaticos: nenhum pode sobreviver a este robo
		Transformacao.CorDoCorpoIgnoradaDeTeste = false;
		CargaVisual.ChamaSoComOCDeTeste = false;
		Iluminacao.MeioDiaDeTeste = false;
		if (!_assinou || GameClient.Instance is not { } cli) return;
		cli.FuriaIrrompeu -= AoIrromper;
		cli.FormaMudou -= AoMudarForma;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } cli || World.Instancia?.PosicaoLocal == null) return;
		if (!_assinou)
		{
			cli.FuriaIrrompeu += AoIrromper;
			cli.FormaMudou += AoMudarForma;
			_assinou = true;
			TravarALuzDoPalco();
		}

		// o pico do Ki na ficha, quadro a quadro -- inclusive nos quadros de espera do roteiro (ver `_picoDoKi`)
		_picoDoKi = Math.Max(_picoDoKi, RazaoDeKiNaFicha(cli));

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print($"\n[draiva] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	/// <summary>
	/// ============================ A LUZ DO PALCO E TRAVADA NO DIA CLARO ============================
	/// A hora que o cliente pinta e a do RELOGIO DO PLANETA em que o corpo esta, e o clima e sorteado em blocos de
	/// seis minutos. O `--horateste 0.5` crava o meio-dia da TERRA, e so na largada: o dia padrao tem 24 minutos
	/// (`Ceu.SegundosPorDia`) e a curva da luz comeca a dourar aos 0,55. E este host e Saiyajin: nasce em Vegeta (na
	/// Terra so quando a classe sorteada e a baixa -- o `despejado` do log), onde a hora local depende do relogio de
	/// parede e o clima natural inclui a chuva de sangue (riscos VERMELHOS na tela inteira, numa bancada que conta
	/// pixel vermelho).
	///
	/// E a luz do ambiente MULTIPLICA tudo o que se desenha, a chama inclusive: na "tarde dourada" da curva do dia
	/// (`f0cfa0`) o azul chega a tela a 63% e o vermelho a 94%, e a chama azul-clara do ki sai BRANCA. Medido na
	/// MESMA chama, com a mesma regua (pixels da caixa com azul dominante): de 24 a 30% numa rodada nublada e 8,1%
	/// numa que caiu de tarde -- a regua da tela estava medindo a hora em que a bancada rodou, e a folga dela
	/// (6 pontos) ia embora sozinha. Com a luz travada a leitura fica em torno de 16%, e foi tirada debaixo de
	/// chuva (o HUD dizia `meio-dia - chuva` e a foto saiu limpa).
	///
	/// A trava (`Iluminacao.MeioDiaDeTeste`) fixa so o AMBIENTE e cala o desenho do clima, no cliente. Nada do que
	/// este robo julga -- o pacote, a cena, o tema, o Ki, a forma, qual das auras acende -- passa pela luz.
	/// ==================================================================================================
	/// </summary>
	private static void TravarALuzDoPalco() => Iluminacao.MeioDiaDeTeste = true;

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[draiva] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[draiva] foto {caminho}");
	}

	/// <summary>As cinematicas vivas em cima deste corpo, agora.</summary>
	private static List<Transformacao> CenasEm(int id)
	{
		var l = new List<Transformacao>();
		if (World.Instancia?.CorpoDeTeste(id) is not { } corpo) return l;
		foreach (Node n in corpo.GetParent().GetChildren())
			if (n is Transformacao t && IsInstanceValid(t) && !t.IsQueuedForDeletion() && t.AlvoDaCena == corpo) l.Add(t);
		return l;
	}

	private static Transformacao? CenaDaFuriaEm(int id) =>
		CenasEm(id).Find(t => ReferenceEquals(t.CenaDeTeste, Cinematicas.Furia));

	private static double RazaoDeKiNaFicha(GameClient cli) => cli.Sheet.Ki / Math.Max(cli.Sheet.MaxKi, 1e-9);

	private static bool TemaDaRaivaNoAr(AudioDirector som) =>
		som.CamadaDeTeste == AudioDirector.Camada.Raiva && som.FaixaDeTeste.Contains("Anger Theme", StringComparison.Ordinal);

	// =====================================================================
	// AS DUAS AURAS DO CORPO, E O PIXEL
	// =====================================================================

	/// <summary>A chama de CARGA do corpo local -- a aura normal: a que o C acende, e a que o Ki acima do tanque acende sozinho.</summary>
	private static CargaVisual? AuraNormalDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CargaVisual>("Carga");

	/// <summary>A cor da chama que o corpo tem agora (a pessoal na base, a da forma em que ele estiver).</summary>
	private static Color CorDaAuraDe(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<Aura>("Aura")?.CorDaChama ?? Aura.CorDoKiCru;

	/// <summary>
	/// A aura normal esta ACESA PELO EXCESSO de Ki, sem a tecla C: o estado do node e a forca que o shader recebeu.
	/// </summary>
	private static (bool Acesa, bool PeloExcesso, bool SegurandoC, float Forca) AuraNormal(int id)
	{
		CargaVisual? c = AuraNormalDe(id);
		return c == null
			? (false, false, false, 0f)
			: (c.CarregandoDeTeste, c.ExcessoDeTeste, c.SegurandoDeTeste, c.DesenhoDeTeste.ForcaNoMaterialDeTeste ?? 0f);
	}

	private static bool MesmaCor(Color a, Color b) =>
		Mathf.Abs(a.R - b.R) < 0.03f && Mathf.Abs(a.G - b.G) < 0.03f && Mathf.Abs(a.B - b.B) < 0.03f;

	/// <summary>
	/// ============================ O QUE ESTA DESENHADO EM VOLTA DO CORPO, LIDO DA TELA ============================
	/// Duas contagens sobre o quadro ja desenhado, e nenhuma confia em campo do cliente:
	///
	///   * VERMELHOS -- pixels vermelho-dominantes numa caixa larga (3 tiles pra esquerda e pra cima, onde cabem a
	///     chama antiga e meio anel de choque). So pra ESQUERDA: a direita ficam o amigo e o algoz;
	///   * DA COR DO KI -- a fracao de pixels de azul dominante numa caixa justa em cima do corpo, que e onde a
	///     chama da aura desenha. O azul-claro e a cor que o PALCO deu a este corpo (`PalcoDaRaivaDeTeste`); grama,
	///     pele, cabelo, poeira e cratera nao tem azul dominante, entao sem chama a fracao e zero. (A primeira
	///     versao contava pixels CLAROS numa caixa uma vez e meia maior e ficava com o menor de tres quadros: leu
	///     1,2% em paz e 2,7% com a aura acesa -- nao separava nada.) "Azul dominante" so quer dizer "a chama"
	///     enquanto a luz do ambiente for neutra, e por isso ela e travada: ver <see cref="TravarALuzDoPalco"/>.
	///
	/// As duas servem pra COMPARAR (antes x durante, cena limpa x defeito injetado): o corpo tem roupa, e a roupa
	/// pode ter vermelho -- o numero absoluto nao quer dizer nada.
	/// ================================================================================================================
	/// </summary>
	private (int Vermelhos, float DaCorDoKi)? PixelsEmVoltaDeMim()
	{
		Viewport? v = GetViewport();
		Image? img = v?.GetTexture()?.GetImage();
		if (v == null || img == null || img.IsEmpty() || World.Instancia?.PosicaoLocal is not { } eu) return null;

		Transform2D praTela = v.GetFinalTransform() * v.CanvasTransform;
		Vector2 pes = praTela * eu;
		float tile = Jandirus.Core.World.ZoneCollision.TileSize * praTela.Scale.X;
		int larg = img.GetWidth(), alt = img.GetHeight();
		_ondeLi = $"corpo em ({pes.X:0},{pes.Y:0}) de uma tela de {larg}x{alt}, tile de {tile:0} px";

		int Dentro(float valor, int teto) => Math.Clamp((int)valor, 0, teto);

		// AS DUAS CAIXAS, EM TILES (na janela da bancada o tile tem 96 px: 1280x720 com o zoom 3 do jogo).
		//   * a larga: 3,2 tiles pra esquerda e 3,4 pra cima (o anel de choque abre ate 3 tiles), 1 pra direita (o
		//     amigo fica a 2) e 0,6 pra baixo (logo abaixo comeca o painel do chat);
		//   * a justa: a chama de carga cabe em +-1,2 tile de largura e 1,5 tile acima do corpo -- a caixa pega o
		//     miolo dela, +-0,75 tile por 2 de altura, onde a chama enche um quinto dos pixels.
		int vermelhos = 0;
		for (int y = Dentro(pes.Y - 3.4f * tile, alt); y < Dentro(pes.Y + 0.6f * tile, alt); y += 2)
			for (int x = Dentro(pes.X - 3.2f * tile, larg); x < Dentro(pes.X + 1.0f * tile, larg); x += 2)
			{
				Color c = img.GetPixel(x, y);
				if (c.R > 0.35f && c.R > c.G * 1.6f && c.R > c.B * 1.6f) vermelhos++;
			}

		int daCor = 0, lidos = 0;
		for (int y = Dentro(pes.Y - 1.75f * tile, alt); y < Dentro(pes.Y + 0.3f * tile, alt); y += 2)
			for (int x = Dentro(pes.X - 0.75f * tile, larg); x < Dentro(pes.X + 0.75f * tile, larg); x += 2)
			{
				Color c = img.GetPixel(x, y);
				lidos++;
				// AZUL DOMINANTE, e nao "azul claro": o piso de brilho e baixo de proposito -- a chama e translucida
				// e so o miolo dela e claro. Com a luz travada no dia claro ela enche uns 16% da caixa, contra 0,0%
				// sem ela (o corte da checagem fica em 6 pontos acima do que se le em paz).
				if (c.B > 0.30f && c.B > c.R * 1.15f && c.B > c.G * 0.95f) daCor++;
			}
		return (vermelhos, lidos > 0 ? daCor / (float)lidos : 0f);
	}

	/// <summary>
	/// A fracao de pixels DA COR DO KI em cima do corpo -- a MEDIANA de cinco quadros. A chama e animada (ha quadros
	/// em que ela afina), o anel de choque da furia passa pela caixa na mesma cor por um instante, e num dia de
	/// chuva caem riscos azuis: a mediana fica com o que a caixa mostra na maior parte do tempo.
	/// </summary>
	private IEnumerable MedirACorDoKi(float[] onde)
	{
		var amostras = new List<float>();
		for (int i = 0; i < 5; i++)
		{
			if (PixelsEmVoltaDeMim() is { } p) amostras.Add(p.DaCorDoKi);
			yield return 0.1;
		}
		amostras.Sort();
		onde[0] = amostras.Count == 0 ? -1f : amostras[amostras.Count / 2];
	}

	/// <summary>
	/// O PICO DE VERMELHO DE UMA CENA DE FURIA: o anel de choque e forte so no comeco da vida dele (o alfa cai com o
	/// quadrado do tempo), entao a leitura e feita logo DEPOIS de um anel nascer -- em dois aneis seguidos, e fica
	/// o maior numero. Resultado em <see cref="_vermelhosNoPico"/> (-1 = nao deu pra medir).
	/// </summary>
	private IEnumerable MedirOVermelhoDaCena(Transformacao cena)
	{
		_vermelhosNoPico = -1;
		for (int anel = 0; anel < 2; anel++)
		{
			int tinha = cena.AneisDeTeste;
			for (int q = 0; q < 90 && IsInstanceValid(cena) && cena.AneisDeTeste == tinha; q++) yield return 0.0;
			if (!IsInstanceValid(cena)) yield break;
			for (int amostra = 0; amostra < 3; amostra++)
			{
				yield return 0.03;
				if (PixelsEmVoltaDeMim() is { } p) _vermelhosNoPico = Math.Max(_vermelhosNoPico, p.Vermelhos);
			}
		}
	}

	private IEnumerable EsperarACenaDaFuriaAcabar(int eu)
	{
		for (int i = 0; i < 90 && CenaDaFuriaEm(eu) != null; i++) yield return 0.1;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		World mundo = World.Instancia!;
		int eu = cli.LocalId;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }
		if (AudioDirector.Instance is not { } som) { Afirmar("(montagem) ha um diretor de som", false); yield break; }
		var semForma = new Color(Cinematicas.CorDaCenaSemForma);
		float[] corDoKi = new float[1];

		// ------------------------------------------------------------ 0. o palco
		(int Amigo, int Algoz)? palco = srv.PalcoDaRaivaDeTeste(eu);
		yield return 2.0;
		Afirmar("(montagem) o palco pos o amigo declarado e o algoz ao lado do host, e os dois estao na tela",
				palco != null && mundo.CorpoDeTeste(palco.Value.Amigo) != null && mundo.CorpoDeTeste(palco.Value.Algoz) != null);
		if (palco == null) yield break;
		Afirmar("(montagem) em paz: nenhuma cena de furia, o tema da raiva calado, e o Ki do dono em ~20% do tanque",
				CenaDaFuriaEm(eu) == null && !TemaDaRaivaNoAr(som) && RazaoDeKiNaFicha(cli) < 0.6,
				$"camada {som.CamadaDeTeste}, Ki {RazaoDeKiNaFicha(cli) * 100:0}%");
		string arquivoDoTema = $"res://Assets/Sounds/Music/{Cinematicas.Furia.Musica}";
		Afirmar("o arquivo do tema da raiva (o do DM, `Murder.dm:144`) esta no projeto e o Godot o resolve",
				ResourceLoader.Exists(arquivoDoTema), arquivoDoTema);

		Color auraDoCorpo = CorDaAuraDe(eu);
		Afirmar("(montagem) a chama deste corpo NAO e vermelha (o palco deu a ele o azul-claro do ki): a regua de 'nada vermelho' tem o que medir",
				!MesmaCor(auraDoCorpo, semForma) && auraDoCorpo.B > auraDoCorpo.R,
				$"aura {auraDoCorpo.ToHtml(false)}, vermelho das cenas sem forma {semForma.ToHtml(false)}");
		float escuridaoDoDiaClaro = 1 - Iluminacao.AmbienteDia.Luminance;
		Afirmar("(montagem) a luz do palco esta travada no dia claro -- a regua da tela nao mede a hora nem o clima do planeta",
				Iluminacao.MeioDiaDeTeste && Mathf.Abs(Iluminacao.Escuridao - escuridaoDoDiaClaro) < 0.01f,
				$"escuridao do ambiente {Iluminacao.Escuridao:0.00} (a do dia claro e {escuridaoDoDiaClaro:0.00})");
		foreach (object passo in MedirACorDoKi(corDoKi)) yield return passo;
		float kiNaTelaEmPaz = corDoKi[0];
		(int Vermelhos, float DaCorDoKi)? emPazNaTela = PixelsEmVoltaDeMim();
		(bool Acesa, bool PeloExcesso, bool SegurandoC, float Forca) auraEmPaz = AuraNormal(eu);
		Afirmar("(montagem) em paz, com o Ki abaixo do tanque, NAO ha aura nenhuma no corpo -- e a tela foi lida",
				!auraEmPaz.Acesa && emPazNaTela != null && kiNaTelaEmPaz >= 0 && kiNaTelaEmPaz < 0.03f,
				$"aura acesa={auraEmPaz.Acesa}; pixels da cor do ki em cima do corpo {kiNaTelaEmPaz * 100:0.0}%, vermelhos em volta {emPazNaTela?.Vermelhos}; {_ondeLi}");

		// ------------------------------------------------------------ 1. o amigo MORRE na frente dele
		GD.Print("[draiva] -- 1) o amigo MORRE na frente dele (sem ter caido antes) --");
		int presosAntes = Transformacao.PresosDeTeste;
		int vermelhosAntes = emPazNaTela?.Vermelhos ?? 0;
		_picoDoKi = 0;
		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 0.1;
		Transformacao? cena = null;
		for (int i = 0; i < 20 && (cena = CenaDaFuriaEm(eu)) == null; i++) yield return 0.0;
		if (cena != null) foreach (object passo in MedirOVermelhoDaCena(cena)) yield return passo;

		Afirmar("**O PACOTE DA FURIA CHEGA** ao cliente, um so, e e o do dono", _furias.Count == 1 && _furias[0] == eu,
				$"{_furias.Count} pacote(s)");
		Afirmar("**A CINEMATICA DA FURIA NASCE** em cima do corpo dele", cena != null, $"{CenasEm(eu).Count} cena(s) no corpo");
		Afirmar("...e NAO prende o corpo (`set waitfor = 0`, `Murder.dm:137`)", Transformacao.PresosDeTeste == presosAntes,
				$"presos {Transformacao.PresosDeTeste} (eram {presosAntes})");
		Afirmar("**O TEMA DA RAIVA ENTRA**, na camada da raiva (entre o combate e a transformacao)", TemaDaRaivaNoAr(som),
				$"camada {som.CamadaDeTeste}, faixa '{som.FaixaDeTeste.GetFile()}'");
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? noTopo = srv.RaivaDeTeste(eu);
		Afirmar("o servidor diz FURIA EXTREMA", noTopo?.Nivel == NivelDeRaiva.Extrema, noTopo?.Nivel.ToString() ?? "?");
		Afirmar("**O KI DO DONO SALTA PRO TETO DE CARGA** -- na ficha que chegou pelo fio (a barra do HUD): o pico encosta no teto, e dali em diante so escorre",
				OKiSaltouProTeto(cli) && cli.Sheet.TetoKi > 1.01, OKiEmTexto(cli));

		// ---- A CENA SEM VERMELHO (dono, 2026-10-11; a divergencia do `Murder.dm:146-150` esta em `Cinematicas.Furia`) ----
		if (cena != null && IsInstanceValid(cena))
		{
			float? forcaDaChama = cena.ChamaDaCenaDeTeste.ForcaNoMaterialDeTeste;
			Afirmar("**A CENA DA FURIA NAO ACENDE CHAMA PROPRIA** (a aura vermelha saiu): a forca da chama da cena, no shader, e zero -- e as ondas de choque ja sairam",
					forcaDaChama is 0f && cena.AneisDeTeste >= 2,
					$"forca {forcaDaChama?.ToString("0.###") ?? "sem material"}, {cena.AneisDeTeste} anel(is) aos {cena.TempoDeTeste:0.0}s");
			Afirmar("**...e os aneis de choque saem na cor da aura do PROPRIO corpo, e nao em vermelho**",
					MesmaCor(cena.CorDosAneisDeTeste, auraDoCorpo) && !MesmaCor(cena.CorDosAneisDeTeste, semForma),
					$"aneis {cena.CorDosAneisDeTeste.ToHtml(false)}, aura do corpo {auraDoCorpo.ToHtml(false)}");
		}
		int vermelhosDaCenaLimpa = _vermelhosNoPico;
		Afirmar("**NA TELA: a cena nao pos vermelho nenhum em volta do corpo** (pixels vermelho-dominantes, antes x no pico dos aneis)",
				vermelhosDaCenaLimpa >= 0 && vermelhosDaCenaLimpa - vermelhosAntes <= 40,
				$"{vermelhosAntes} em paz -> {vermelhosDaCenaLimpa} com os aneis na tela");

		// ---- A AURA NORMAL, PELO KI ACIMA DO TANQUE E SEM TECLA NENHUMA ----
		foreach (object passo in MedirACorDoKi(corDoKi)) yield return passo;
		float kiNaTelaComAAura = corDoKi[0];
		(bool Acesa, bool PeloExcesso, bool SegurandoC, float Forca) aura = AuraNormal(eu);
		Afirmar("**A AURA QUE SE VE E A NORMAL DELE: acesa pelo Ki acima de 100%, sem tecla C nenhuma** (`CargaVisual.Definir`: 'C OU excesso')",
				aura.Acesa && aura.PeloExcesso && !aura.SegurandoC && aura.Forca > 0.05f,
				$"acesa={aura.Acesa}, excesso={aura.PeloExcesso}, segurando C={aura.SegurandoC}, forca no shader {aura.Forca:0.##}");
		Afirmar("**NA TELA: ha uma chama da cor do ki dele desenhada em cima do corpo, que em paz nao havia**",
				kiNaTelaComAAura >= kiNaTelaEmPaz + 0.06f,
				$"pixels da cor do ki em cima do corpo: {kiNaTelaEmPaz * 100:0.0}% em paz -> {kiNaTelaComAAura * 100:0.0}% agora");
		Fotografar("raiva-01-a-cena-sem-vermelho-com-a-aura-normal-do-ki-acima-de-100");

		// andar no meio dela
		Vector2 antesDeAndar = mundo.PosicaoLocal ?? Vector2.Zero;
		mundo.AndarDeTeste(Vector2.Down);
		yield return 0.7;
		mundo.PararDeTeste();
		Vector2 depoisDeAndar = mundo.PosicaoLocal ?? Vector2.Zero;
		Afirmar("**DA PRA ANDAR NO MEIO DA CENA** (ela acontece com voce no meio da luta, e nao te tira dela)",
				depoisDeAndar.DistanceTo(antesDeAndar) > 24f && CenaDaFuriaEm(eu) != null,
				$"andou {depoisDeAndar.DistanceTo(antesDeAndar):0} px");

		for (int i = 0; i < 60 && cena != null && IsInstanceValid(cena) && cena.TempoDeTeste < 3.3; i++) yield return 0.05;
		Fotografar("raiva-02-a-erupcao-e-a-cratera");
		Afirmar("o tema esta TOCANDO de verdade (o arquivo chegou da thread de carga e o tocador ligou)", som.TocandoDeTeste);

		float chamaNoFim = cena != null && IsInstanceValid(cena) ? cena.ChamaDaCenaDeTeste.ForcaNoMaterialDeTeste ?? -1f : -1f;
		Afirmar("...e depois da erupcao a chama da cena continua apagada (ela nunca acendeu)", chamaNoFim is 0f, $"forca {chamaNoFim:0.###}");
		foreach (object passo in EsperarACenaDaFuriaAcabar(eu)) yield return passo;
		Afirmar("a cena ACABA sozinha (5,0 s)", CenaDaFuriaEm(eu) == null);
		yield return 1.5;
		Afirmar("...e o tema CONTINUA depois dela (no DM ele dura a faixa inteira, 245 s), com UM tocador so no ar",
				TemaDaRaivaNoAr(som) && som.TocandoDeTeste && som.TocadoresNoArDeTeste == 1,
				$"camada {som.CamadaDeTeste}, {som.TocadoresNoArDeTeste} tocador(es)");
		aura = AuraNormal(eu);
		Afirmar("...e a aura normal continua acesa depois da cena: ela e do Ki (que ainda esta acima do tanque), nao da cinematica",
				aura.Acesa && aura.PeloExcesso && RazaoDeKiNaFicha(cli) > 1.01, $"acesa={aura.Acesa}, Ki {RazaoDeKiNaFicha(cli) * 100:0}%");

		// ------------------------------------------------------------ 1b. MEDITAR ACALMA (o tema fica, o Ki fica)
		GD.Print("[draiva] -- 1b) ele senta pra meditar: a raiva se desfaz, o tema continua, o Ki fica --");
		double kiAoSentar = RazaoDeKiNaFicha(cli);
		bool sentou = srv.MeditarDeTeste(eu, sentar: true);
		yield return 0.8;
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? calmo = srv.RaivaDeTeste(eu);
		Afirmar("**MEDITAR ACALMA** (`Meditate.dm:27-31`): o servidor aceitou a meditacao e a furia extrema acabou na hora",
				sentou && calmo?.Nivel == NivelDeRaiva.Nenhuma && Math.Abs((calmo?.Raiva ?? 0) - 100) < 0.001,
				$"sentou={sentou}, {calmo?.Nivel}, raiva {calmo?.Raiva:0.#}");
		Afirmar("...o TEMA da raiva continua tocando (so um tema de transformacao ou outra furia o calam, `BattleMusic.dm:138` e `:151`)",
				TemaDaRaivaNoAr(som) && som.TocandoDeTeste, $"camada {som.CamadaDeTeste}");
		aura = AuraNormal(eu);
		// O QUE "NAO E DEVOLVIDO" QUER DIZER DESDE 2026-10-13: o Ki acima do tanque ESCORRE sozinho, sentado ou de pe
		// (item 33 do pedido da v0.2.1 -- ver `_picoDoKi`). Ate ali esta linha cobrava "a barra continua no teto", e isso
		// deixou de ser a regra: uns oito segundos depois do salto a barra esta em ~125%. O que meditar NAO pode fazer e
		// TIRAR o Ki: ele continua bem acima do tanque, e nos 0,8 s sentado so perdeu o que o vazamento explica (um ponto
		// e meio) -- e nao os vinte e tantos de um Ki devolvido aos 100%.
		double kiSentado = RazaoDeKiNaFicha(cli);
		Afirmar("...e o Ki que a furia deu NAO e devolvido: meditar nao o derruba pro tanque -- a barra continua acima dos 100% (so escorrendo, como todo Ki acima do tanque) e a aura normal continua acesa",
				kiSentado > 1.05 && kiSentado > kiAoSentar - 0.05 && aura.Acesa,
				$"Ki {kiAoSentar * 100:0}% ao sentar -> {kiSentado * 100:0}% sentado, aura acesa={aura.Acesa}");
		srv.MeditarDeTeste(eu, sentar: false);

		// ------------------------------------------------------------ 2. a luta de verdade: cai, e depois morre
		GD.Print("[draiva] -- 2) a luta de verdade: o amigo CAI, e so depois morre --");
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		som.PararCamada(AudioDirector.Camada.Raiva, "bancada: entre dois roteiros");
		_furias.Clear();
		yield return 1.0;
		Afirmar("(montagem) com o Ki de volta abaixo do tanque a aura normal APAGA (ela e do Ki)", !AuraNormal(eu).Acesa);

		srv.AbaterOAmigoDeTeste(morreu: false);
		yield return 1.2;
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? comum = srv.RaivaDeTeste(eu);
		Afirmar("o amigo CAI: o servidor diz raiva COMUM, e ela vale MENOS que a extrema",
				comum?.Nivel == NivelDeRaiva.Comum && noTopo != null && comum.Value.Raiva < noTopo.Value.Raiva - 0.5,
				$"{comum?.Nivel}, raiva {comum?.Raiva:0.#} contra {noTopo?.Raiva:0.#} no topo");
		Afirmar("**...e NADA acende: nenhum pacote de furia, nenhuma cena, o tema calado, o Ki onde estava, nenhuma aura**",
				_furias.Count == 0 && CenaDaFuriaEm(eu) == null && !TemaDaRaivaNoAr(som) && RazaoDeKiNaFicha(cli) < 0.6 && !AuraNormal(eu).Acesa,
				$"{_furias.Count} pacote(s), camada {som.CamadaDeTeste}, Ki {RazaoDeKiNaFicha(cli) * 100:0}%");
		Fotografar("raiva-03-o-amigo-caido-so-raiva-comum");

		_picoDoKi = 0;
		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 0.5;
		cena = CenaDaFuriaEm(eu);
		Afirmar("**...e ele MORRE: a cinematica e o tema VEM, mesmo pra quem ja estava com raiva do nocaute**",
				_furias.Count == 1 && cena != null && TemaDaRaivaNoAr(som),
				$"{_furias.Count} pacote(s), cena {(cena != null ? "viva" : "nenhuma")}, camada {som.CamadaDeTeste}");
		Afirmar("...e o Ki salta pro teto de novo", OKiSaltouProTeto(cli), OKiEmTexto(cli));
		for (int i = 0; i < 40 && cena != null && IsInstanceValid(cena) && cena.TempoDeTeste < 1.5; i++) yield return 0.05;
		Fotografar("raiva-04-caiu-e-morreu-a-cena-vem");
		foreach (object passo in EsperarACenaDaFuriaAcabar(eu)) yield return passo;

		// ------------------------------------------------------------ 2b. os dois contra-exemplos da tela
		// A MESMA cena, com os dois defeitos de volta: os aneis no vermelho das cenas sem forma (o port ate
		// 2026-10-11) e a chama de carga so com a tecla C (a regra antiga da `CargaVisual`).
		GD.Print("[draiva] -- 2b) contra-exemplos: a cena VERMELHA e a aura SO com a tecla C --");
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		yield return 1.0;
		(int Vermelhos, float DaCorDoKi)? antesDoDefeito = PixelsEmVoltaDeMim();
		Transformacao.CorDoCorpoIgnoradaDeTeste = true;
		CargaVisual.ChamaSoComOCDeTeste = true;
		try
		{
			srv.AbaterOAmigoDeTeste(morreu: true);
			yield return 0.1;
			cena = null;
			for (int i = 0; i < 20 && (cena = CenaDaFuriaEm(eu)) == null; i++) yield return 0.0;
			if (cena != null) foreach (object passo in MedirOVermelhoDaCena(cena)) yield return passo;
			int vermelhosComDefeito = _vermelhosNoPico - (antesDoDefeito?.Vermelhos ?? 0);
			Afirmar("(defeito injetado: a furia volta a cair na cor das cenas sem forma) os aneis nascem VERMELHOS -- a checagem da cor REPROVA",
					cena != null && MesmaCor(cena.CorDosAneisDeTeste, semForma), cena?.CorDosAneisDeTeste.ToHtml(false) ?? "sem cena");
			Afirmar("(o mesmo defeito, NA TELA) aparecem pixels vermelhos em volta do corpo -- a checagem 'nenhum vermelho' REPROVA, e a regua enxerga",
					vermelhosComDefeito > 40 && vermelhosComDefeito > (vermelhosDaCenaLimpa - vermelhosAntes) * 3,
					$"+{vermelhosComDefeito} com o defeito, contra +{vermelhosDaCenaLimpa - vermelhosAntes} na cena limpa");
			yield return 0.6;
			aura = AuraNormal(eu);
			Afirmar("(defeito injetado: a chama de carga so acende com a tecla C) com o Ki no teto NAO ha aura -- a checagem da aura normal REPROVA",
					!aura.Acesa && aura.PeloExcesso && RazaoDeKiNaFicha(cli) > 1.01,
					$"acesa={aura.Acesa}, excesso={aura.PeloExcesso}, Ki {RazaoDeKiNaFicha(cli) * 100:0}%");
			foreach (object passo in MedirACorDoKi(corDoKi)) yield return passo;
			Afirmar("(o mesmo defeito, NA TELA) nao ha chama da cor do ki em cima do corpo -- a checagem da tela REPROVA, e a regua enxerga a falta",
					corDoKi[0] >= 0 && corDoKi[0] < kiNaTelaEmPaz + 0.06f,
					$"{corDoKi[0] * 100:0.0}% com o defeito, contra {kiNaTelaComAAura * 100:0.0}% com a aura acesa");
			foreach (object passo in EsperarACenaDaFuriaAcabar(eu)) yield return passo;
		}
		finally
		{
			Transformacao.CorDoCorpoIgnoradaDeTeste = false;
			CargaVisual.ChamaSoComOCDeTeste = false;
		}

		// ------------------------------------------------------------ 2c. SEM controle de ki: a cena, e nenhuma aura
		GD.Print("[draiva] -- 2c) sem o controle de ki: a furia vem, o Ki nao salta, e nao ha aura nenhuma --");
		srv.ControleDeKiDeTeste(eu, tem: false);
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		_furias.Clear();
		yield return 1.0;
		foreach (object passo in MedirACorDoKi(corDoKi)) yield return passo;
		float kiNaTelaAntesSemControle = corDoKi[0];
		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 0.6;
		cena = CenaDaFuriaEm(eu);
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? semSalto = srv.RaivaDeTeste(eu);
		Afirmar("SEM o controle de ki: a furia extrema e a cinematica vem do mesmo jeito, e o Ki fica onde estava",
				_furias.Count == 1 && cena != null && semSalto?.Nivel == NivelDeRaiva.Extrema && RazaoDeKiNaFicha(cli) < 0.6,
				$"{_furias.Count} pacote(s), {semSalto?.Nivel}, Ki {RazaoDeKiNaFicha(cli) * 100:0}%");
		for (int i = 0; i < 40 && cena != null && IsInstanceValid(cena) && cena.TempoDeTeste < 1.3; i++) yield return 0.05;
		foreach (object passo in MedirACorDoKi(corDoKi)) yield return passo;
		aura = AuraNormal(eu);
		float? chamaSemControle = cena != null && IsInstanceValid(cena) ? cena.ChamaDaCenaDeTeste.ForcaNoMaterialDeTeste : null;
		Afirmar("**...e ele fica SEM AURA NENHUMA: nem a normal (o Ki nao passou do tanque), nem a da cena**",
				!aura.Acesa && !aura.PeloExcesso && chamaSemControle is 0f,
				$"aura normal acesa={aura.Acesa}, chama da cena {chamaSemControle?.ToString("0.###") ?? "?"}");
		Afirmar("**NA TELA: nenhuma chama em cima do corpo** (os pixels da cor do ki nao sobem como subiram com a aura)",
				corDoKi[0] >= 0 && corDoKi[0] < kiNaTelaAntesSemControle + 0.03f && corDoKi[0] < kiNaTelaComAAura * 0.5f,
				$"{kiNaTelaAntesSemControle * 100:0.0}% antes -> {corDoKi[0] * 100:0.0}% na cena (com a aura eram {kiNaTelaComAAura * 100:0.0}%)");
		Fotografar("raiva-05-sem-controle-de-ki-a-cena-sem-aura-nenhuma");
		foreach (object passo in EsperarACenaDaFuriaAcabar(eu)) yield return passo;
		srv.ControleDeKiDeTeste(eu, tem: true);

		// ------------------------------------------------------------ 3. com o requisito da forma, ele se transforma SOZINHO
		// O tema da raiva do passo anterior continua no ar (ele dura 245 s). O dono se acalma, ganha o poder do Super
		// Saiyajin, e ve o amigo morrer DE NOVO: agora a furia cumpre o requisito de uma forma que ele nunca teve.
		// NINGUEM APERTA C daqui pra frente -- este robo nao manda pedido de transformacao nenhum.
		GD.Print("[draiva] -- 3) com o requisito do Super Saiyajin cumprido: a furia o transforma SOZINHO, e a cena e a da forma --");
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		srv.DarPoderPraFormaDeTeste(eu, "ssj1", "ssj2");
		_furias.Clear();
		_formas.Clear();
		yield return 0.8;
		(string Forma, bool Despertou)? emPaz = srv.FormaDeTeste(eu, "ssj1");
		Afirmar("(montagem) em paz, com o poder do Super Saiyajin na mao, ele continua na base e nunca despertou a forma -- e o tema da raiva de antes ainda toca",
				emPaz is { Forma: "base", Despertou: false } && TemaDaRaivaNoAr(som),
				$"forma {emPaz?.Forma}, despertou={emPaz?.Despertou}, camada {som.CamadaDeTeste}");

		_picoDoKi = 0;
		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 1.0;
		(string Forma, bool Despertou)? virou = srv.FormaDeTeste(eu, "ssj1");
		List<Transformacao> cenas = CenasEm(eu);
		Afirmar("**ELE VIRA SUPER SAIYAJIN SOZINHO** -- ninguem apertou nada -- e a cinematica no corpo e a da TRANSFORMACAO",
				virou is { Forma: "ssj1", Despertou: true } && cenas.Exists(t => !ReferenceEquals(t.CenaDeTeste, Cinematicas.Furia)),
				$"forma {virou?.Forma}, {cenas.Count} cena(s) no corpo");
		Afirmar("**...e a cinematica da RAIVA nao toca: nenhum pacote de furia chegou, nenhuma cena de furia nasceu (nunca as duas)**",
				_furias.Count == 0 && CenaDaFuriaEm(eu) == null, $"{_furias.Count} pacote(s) de furia");
		Afirmar("...o Ki saltou pro teto e atravessou a transformacao", OKiSaltouProTeto(cli), OKiEmTexto(cli));
		Afirmar("**O TEMA DA TRANSFORMACAO TOMA O LUGAR DO DA RAIVA** (`BattleMusic.dm:138`: a transformation always wins)",
				som.CamadaDeTeste == AudioDirector.Camada.Transformacao && som.FaixaDeTeste.Length > 0
				&& !som.FaixaDeTeste.Contains("Anger Theme", StringComparison.Ordinal),
				$"camada {som.CamadaDeTeste}, faixa '{som.FaixaDeTeste.GetFile()}'");
		yield return 2.5;
		Afirmar("...e passado o cruzamento ha UM tocador no ar: os dois temas nao se sobrepoem",
				som.TocandoDeTeste && som.TocadoresNoArDeTeste == 1, $"{som.TocadoresNoArDeTeste} tocador(es)");
		Fotografar("raiva-06-com-o-requisito-a-cena-e-a-da-transformacao");

		bool adiantou = som.AdiantarParaOFimDeTeste(0.4);
		yield return 2.0;
		Afirmar("**ACABADO O TEMA DA FORMA, O DA RAIVA NAO VOLTA** (o pedido dele saiu junto)",
				adiantou && som.CamadaDeTeste < AudioDirector.Camada.Raiva
				&& !som.FaixaDeTeste.Contains("Anger Theme", StringComparison.Ordinal),
				$"adiantou={adiantou}, camada {som.CamadaDeTeste}, faixa '{som.FaixaDeTeste.GetFile()}'");

		// a estreia do Super Saiyajin corre ate o fim: os dois passos de baixo comecam com o corpo solto
		for (int i = 0; i < 450 && CenasEm(eu).Count > 0; i++) yield return 0.1;
		Afirmar("(montagem) a cinematica de estreia do Super Saiyajin terminou e soltou o corpo",
				CenasEm(eu).Count == 0 && !Transformacao.PrendendoOCorpo, $"{CenasEm(eu).Count} cena(s), presos {Transformacao.PresosDeTeste}");

		// ------------------------------------------------------------ 4. a forma JA e dele: a cena e a da RAIVA
		GD.Print("[draiva] -- 4) a forma ja despertada, e nenhuma nova a estrear: a cena e a da RAIVA, e ele fica como esta --");
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		srv.VoltarPraBaseDeTeste(eu);
		_furias.Clear();
		_formas.Clear();
		yield return 1.0;
		(string Forma, bool Despertou)? naBase = srv.FormaDeTeste(eu, "ssj1");
		Afirmar("(montagem) ele esta na BASE, com o Super Saiyajin ja despertado e sem poder pro 2",
				naBase is { Forma: "base", Despertou: true }, $"forma {naBase?.Forma}, despertou={naBase?.Despertou}");
		_formas.Clear();

		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 0.8;
		cena = CenaDaFuriaEm(eu);
		(string Forma, bool Despertou)? ficou = srv.FormaDeTeste(eu, "ssj1");
		Afirmar("**FORMA JA DESPERTADA: A CINEMATICA DA RAIVA TOCA** (dono, 2026-10-11) -- o pacote da furia chega, a cena nasce e o tema e o da raiva",
				_furias.Count == 1 && cena != null && TemaDaRaivaNoAr(som),
				$"{_furias.Count} pacote(s) de furia, cena {(cena != null ? "viva" : "nenhuma")}, camada {som.CamadaDeTeste}");
		Afirmar("**...e ele NAO e transformado: continua na base, e nenhum pacote de forma chegou** (a transformacao forcada e so a da ESTREIA)",
				ficou is { Forma: "base" } && _formas.Count == 0 && CenasEm(eu).TrueForAll(t => ReferenceEquals(t.CenaDeTeste, Cinematicas.Furia)),
				$"forma {ficou?.Forma}, {_formas.Count} pacote(s) de forma");
		for (int i = 0; i < 40 && cena != null && IsInstanceValid(cena) && cena.TempoDeTeste < 1.3; i++) yield return 0.05;
		Fotografar("raiva-07-forma-ja-despertada-a-cena-e-a-da-raiva");
		foreach (object passo in EsperarACenaDaFuriaAcabar(eu)) yield return passo;

		// ------------------------------------------------------------ 5. na base, com os requisitos do Super Saiyajin 2
		GD.Print("[draiva] -- 5) na BASE, com os requisitos do Super Saiyajin 2: ele sobe pelo 1 (sem cena) e estreia o 2 --");
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		srv.DarPoderPraFormaDeTeste(eu, "ssj2", "ssj3");
		_furias.Clear();
		_formas.Clear();
		yield return 1.0;
		(string Forma, bool Despertou)? antesDoDois = srv.FormaDeTeste(eu, "ssj2");
		Afirmar("(montagem) na BASE, com o Super Saiyajin ja dele, o poder do 2 pago, e o 2 nunca despertado",
				antesDoDois is { Forma: "base", Despertou: false } && CenasEm(eu).Count == 0,
				$"forma {antesDoDois?.Forma}, ssj2 despertou={antesDoDois?.Despertou}");

		_picoDoKi = 0;
		srv.AbaterOAmigoDeTeste(morreu: true);
		yield return 1.0;
		(string Forma, bool Despertou)? noDois = srv.FormaDeTeste(eu, "ssj2");
		cenas = CenasEm(eu);
		Afirmar("**ELE TERMINA EM SUPER SAIYAJIN 2, SOZINHO** (dono: 'ele vai pro ssj1 e comeca a cinematica do ssj2')",
				noDois is { Forma: "ssj2", Despertou: true }, $"forma {noDois?.Forma}");
		Afirmar("**...PASSANDO PELO SUPER SAIYAJIN: chegam DOIS pacotes de forma -- base -> ssj1 SEM cena, e ssj1 -> ssj2 com a cena de ESTREIA**",
				_formas.Count == 2
				&& _formas[0] is { De: "base", Para: "ssj1", Degrau: DegrauDeCena.Nenhuma }
				&& _formas[1] is { De: "ssj1", Para: "ssj2", Degrau: DegrauDeCena.Estreia },
				_formas.Count == 0 ? "nenhum pacote" : string.Join(" | ", _formas.Select(p => $"{p.De}->{p.Para}/{p.Degrau}")));
		Afirmar("**...a cinematica no corpo e UMA, e e a do SUPER SAIYAJIN 2** -- nem a do 1 (que ja era dele), nem a da raiva",
				cenas.Count == 1 && cenas[0].CenaDeTeste.Forma == "ssj2" && _furias.Count == 0,
				$"{cenas.Count} cena(s): {string.Join(", ", cenas.Select(t => t.CenaDeTeste.Forma.Length > 0 ? t.CenaDeTeste.Forma : "furia"))}; {_furias.Count} pacote(s) de furia");
		Afirmar("...o tema e o da transformacao, e o Ki saltou pro teto e atravessou os dois degraus",
				som.CamadaDeTeste == AudioDirector.Camada.Transformacao && OKiSaltouProTeto(cli),
				$"camada {som.CamadaDeTeste}, {OKiEmTexto(cli)}");
		yield return 2.5;
		Fotografar("raiva-08-na-base-com-os-requisitos-do-ssj2-sobe-pelo-ssj1-e-estreia-o-ssj2");

		// ------------------------------------------------------------ 6. a raiva COMUM tambem transforma -- com o BP maior
		// O dono (2026-10-11): *"o rage normal (ao ver um amigo ser nocauteado mas n morto) tb faz vc se transformar da
		// mesma forma q o extreme, porem o requisito de bp pro rage normal liberar sua proxima transformacao e maior"*.
		// O preco e a porta vezes o agio da forma (`FormaDef.AgioNaRaivaComum`); a regra inteira e medida pelas
		// bancadas `raiva` [13] e `--raivateste` (secao 9) -- aqui e o que chega a TELA de quem joga.
		GD.Print("[draiva] -- 6) o amigo so CAI: entre os dois precos nada muda; com 1,2x a porta ele vira Super Saiyajin sozinho --");
		for (int i = 0; i < 450 && CenasEm(eu).Count > 0; i++) yield return 0.1;   // (a estreia do Super Saiyajin 2 corre ate o fim)
		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		srv.EsquecerAsFormasDeTeste(eu);
		double poderEntre = srv.DarPoderPraRaivaComumDeTeste(eu, "ssj1", noPreco: false);
		yield return 1.0;
		_furias.Clear();
		_formas.Clear();
		(string Forma, bool Despertou)? zerado = srv.FormaDeTeste(eu, "ssj1");
		Afirmar("(montagem) na BASE, sem forma nenhuma despertada, com o poder ENTRE a porta do Super Saiyajin e o preco da raiva comum",
				zerado is { Forma: "base", Despertou: false } && CenasEm(eu).Count == 0
				&& poderEntre > 1.01 && poderEntre < Catalogo.AgioComumDoSaiyajin - 0.01,
				$"forma {zerado?.Forma}, despertou={zerado?.Despertou}, poder {poderEntre:0.00}x a porta");

		srv.AbaterOAmigoDeTeste(morreu: false);
		yield return 1.2;
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? soRaiva = srv.RaivaDeTeste(eu);
		(string Forma, bool Despertou)? naoVirou = srv.FormaDeTeste(eu, "ssj1");
		Afirmar("**O AMIGO SO CAI, COM O PODER ENTRE OS DOIS PRECOS: a raiva comum acende e NADA se transforma** (nenhum pacote de forma, nenhuma cena)",
				soRaiva?.Nivel == NivelDeRaiva.Comum && naoVirou is { Forma: "base", Despertou: false }
				&& _formas.Count == 0 && _furias.Count == 0 && CenasEm(eu).Count == 0,
				$"{soRaiva?.Nivel}, forma {naoVirou?.Forma}, {_formas.Count} pacote(s) de forma, {CenasEm(eu).Count} cena(s)");

		srv.AcalmarDeTeste(eu, kiEm: 0.2);
		double poderNoPreco = srv.DarPoderPraRaivaComumDeTeste(eu, "ssj1", noPreco: true);
		yield return 0.6;
		_furias.Clear();
		_formas.Clear();
		srv.AbaterOAmigoDeTeste(morreu: false);
		yield return 1.0;
		(string Forma, bool Despertou)? virouComAComum = srv.FormaDeTeste(eu, "ssj1");
		(NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? raivaNaForma = srv.RaivaDeTeste(eu);
		cenas = CenasEm(eu);
		Afirmar("**COM 1,2x A PORTA, O AMIGO SO CAI E ELE VIRA SUPER SAIYAJIN SOZINHO** -- com a raiva COMUM, e pela estreia: um pacote de forma, base -> ssj1",
				virouComAComum is { Forma: "ssj1", Despertou: true } && raivaNaForma?.Nivel == NivelDeRaiva.Comum
				&& poderNoPreco >= Catalogo.AgioComumDoSaiyajin
				&& _formas.Count == 1 && _formas[0] is { De: "base", Para: "ssj1", Degrau: DegrauDeCena.Estreia },
				$"forma {virouComAComum?.Forma}, {raivaNaForma?.Nivel}, poder {poderNoPreco:0.00}x a porta; "
				+ (_formas.Count == 0 ? "nenhum pacote" : string.Join(" | ", _formas.Select(p => $"{p.De}->{p.Para}/{p.Degrau}"))));
		Afirmar("**...a cinematica no corpo e a da TRANSFORMACAO: nenhum pacote de furia, nenhuma cena de furia, e o tema da raiva NAO toca**",
				cenas.Count == 1 && cenas[0].CenaDeTeste.Forma == "ssj1" && _furias.Count == 0 && !TemaDaRaivaNoAr(som),
				$"{cenas.Count} cena(s): {string.Join(", ", cenas.Select(t => t.CenaDeTeste.Forma.Length > 0 ? t.CenaDeTeste.Forma : "furia"))}; "
				+ $"{_furias.Count} pacote(s) de furia; camada {som.CamadaDeTeste}");
		Afirmar("**...e o Ki NAO salta pro teto**: a barra fica no tanque cheio da estreia (~100%), e nao nos 140% que a furia extrema daria",
				RazaoDeKiNaFicha(cli) > 0.9 && RazaoDeKiNaFicha(cli) < 1.05 && cli.Sheet.TetoKi > 1.3,
				$"Ki {RazaoDeKiNaFicha(cli) * 100:0}% do tanque (teto {cli.Sheet.TetoKi * 100:0}%)");
		yield return 2.5;
		Fotografar("raiva-09-o-amigo-so-cai-e-a-raiva-comum-transforma-com-o-bp-maior");

		srv.LimparPalcoDaRaivaDeTeste(eu);
	}
}
