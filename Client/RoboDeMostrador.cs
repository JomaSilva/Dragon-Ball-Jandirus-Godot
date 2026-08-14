using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO MOSTRADOR (`--diagmostrador`). Ela FOTOGRAFA a HUD -- e so por isso existe.
///
/// ============================ POR QUE ELA E SEPARADA DAS CHECAGENS QUE JA PASSAM ============================
/// Os quatro pedidos desta rodada sao literalmente sobre **o que o dono ve o tempo todo**:
///
///   1. *"a barra de ki no GUI do jogo n mostra quando ta acima de 100%"* -- e ele frisa que no menu
///      P o numero esta certo. Ou seja: DUAS TELAS DISCORDAM, e a que mente e a que fica na cara.
///   2. *"n tem a barra de nutricao"*.
///   3. *"nem a % de bp efetivo ao lado do bp"* -- e a definicao dele: nao muda ao transformar, muda
///      quando cai Ki, estamina, peso ou gravidade.
///   4. *"na janela de FORMS do menu P deveria ter o MULTIPLICADOR TOTAL"*.
///
/// A bancada `--diagnebulosa` ja cobre o item 1 em NUMERO, lendo `Hud.BarraDeKi.TextoDeTeste`. Isso e
/// necessario e nao e suficiente: o texto pode estar certo e a barra desenhada errada (trilho curto,
/// segmento de sobrecarga invisivel, a barra de nutricao montada FORA da tela), e nenhuma leitura de
/// campo distingue esses casos. O historico desta casa e explicito -- quatro defeitos visuais
/// atravessaram mais de quatro mil checagens verdes porque a bancada media a INTENCAO do codigo.
///
/// Entao aqui a regra e outra: **cada afirmacao vem com a foto do quadro que a sustenta.**
/// =========================================================================================================
///
/// ============================ A FOTO CHAVE E A 2, E ELA E O PEDIDO 1 INTEIRO ============================
/// O menu P e um `CanvasLayer` sobre o MESMO viewport da HUD. Entao um unico `GetImage()` com o menu
/// aberto na aba Stats pega **as duas telas que o dono disse discordarem, no mesmo quadro**. Nao ha
/// como uma foto dessas mentir a favor: ou os dois numeros sao o mesmo, ou o defeito esta ali.
/// ======================================================================================================
///
/// COMO RODAR (precisa de JANELA -- no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7985 --kiteste --bpteste 3000000 --diagmostrador
///
/// As fotos saem em `user://mostrador-*.png` (quadro inteiro) e `user://mostrador-*-HUD.png` (o canto
/// esquerdo ampliado, que e onde moram as quatro barras e a linha do BP).
/// </summary>
public partial class RoboDeMostrador : Node
{
	private double _t, _espera = 1.5;
	private int _passo;
	private bool _acabou, _carregando;
	private int _oks;
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;

	private static void Nota(string linha) => GD.Print("[mostrador] " + linha);

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _oks++; else _falhas.Add(oque);
	}

	// =====================================================================
	// AS AMOSTRAS -- o que se compara entre um passo e outro
	// =====================================================================
	/// <summary>
	/// UMA LEITURA DO MOSTRADOR NUM INSTANTE. Guarda o que a FICHA disse **e** o que a BARRA
	/// DESENHOU, lado a lado, porque a diferenca entre os dois foi o bug desta rodada.
	/// </summary>
	private readonly record struct Amostra(
		string Rotulo, double RazaoKi, double TextoDaBarra, double Preenchimento,
		double Inteireza, double MultTotal, double RazaoNutricao);

	private readonly List<Amostra> _amostras = [];

	private Amostra Ler(string rotulo)
	{
		SheetState f = C?.Sheet ?? default;
		Barra? b = Hud.Instancia?.BarraDeKi;

		var a = new Amostra(
			rotulo,
			f.RazaoDeKi,
			TextoEmRazao(b?.TextoDeTeste),
			b?.PreenchimentoDeTeste ?? double.NaN,
			f.Inteireza,
			f.MultTotal,
			f.RazaoDeNutricao);

		_amostras.Add(a);
		Nota($"  --     [{rotulo}] ficha: Ki={a.RazaoKi * 100:0.0}%  inteireza={a.Inteireza * 100:0.0}%  "
		   + $"mult={a.MultTotal:0.###}x  nutricao={a.RazaoNutricao * 100:0.0}%   |   "
		   + $"BARRA escreveu {a.TextoDaBarra * 100:0.0}% e encheu {a.Preenchimento:0.000} do trilho");
		return a;
	}

	/// <summary>
	/// O TEXTO DA BARRA DE VOLTA EM RAZAO. Dar a volta pelo texto e o ponto: e o unico numero desta
	/// bancada que nenhum campo interno pode desmentir -- ele saiu do widget pra a tela.
	/// </summary>
	private static double TextoEmRazao(string? txt)
	{
		if (txt is null) return double.NaN;
		string so = txt.Replace("%", "").Replace(",", ".").Trim();
		return double.TryParse(so, System.Globalization.NumberStyles.Float,
							   System.Globalization.CultureInfo.InvariantCulture, out double v)
			? v / 100.0 : double.NaN;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Copiado do `RoboDeOlhada:112` e pelo mesmo motivo escrito la: com a porta tomada o `--host`
		// nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e esta bancada abre
		// menu, transforma o corpo e mexe no estomago, o que apareceria na tela de quem joga ali.
		// ==================================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[mostrador] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este "
					  + "mundo e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || Hud.Instancia is null || MenuJogo.Instancia is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		// ============================ A ORDEM PAGA O BARATO PRIMEIRO, E ISSO E MEDIDO ============================
		// A nutricao vem ANTES da transformacao de proposito. Este mundo tem varias sessoes rodando ao
		// lado e o processo ja morreu duas vezes no meio da rodada (uma delas antes de a bancada sequer
		// comecar). Uma rodada que gaste os 40 s da cinematica antes de tocar na nutricao perde a prova
		// mais barata quando cai; nesta ordem, uma queda tardia ainda deixa fotos que valem.
		// =======================================================================================================
		switch (_passo++)
		{
			case 0: Preparar(); break;
			case 1: OQueOOlhoVaiJulgar(); break;
			case 2: Repouso(); break;
			case 3: EsvaziarOEstomago(); break;
			case 4: FotoDaNutricaoBaixa(); break;
			case 5: EncherOEstomago(); break;
			case 6: FotoDaNutricaoCheia(); break;
			case 7: ComecarACarga(); break;
			case 8: EsperarOKiPassarDe100(); break;
			case 9: FotoDoKiAcimaDe100(); break;
			case 10: AbrirOMenuNaAbaStats(); break;
			case 11: AsDuasTelasNoMesmoQuadro(); break;
			case 12: AbaForms(); break;
			case 13: FotoDoMultiplicadorNaBase(); break;
			case 14: Transformar(); break;
			case 15: EsperarAForma(); break;
			case 16: FotoDaFormaVestida(); break;
			case 17: EsperarOKiCair(); break;
			case 18: FotoDoKiCaido(); break;
			default: Relatorio(); break;
		}
	}

	// =====================================================================
	// 1. PREPARO
	// =====================================================================
	private void Preparar()
	{
		// O CEU NO MEIO-DIA, e nao e enfeite: um dia dura 24 minutos (`Ceu.SegundosPorDia`) e esta
		// bancada leva alguns. De noite a HUD inteira sai sob um filtro escuro e o dourado do
		// segmento de sobrecarga vira marrom -- a `RoboDeOlhada` ja perdeu duas rodadas inteiras
		// exatamente assim, com o log verde e as fotos imprestaveis.
		C?.SendVerbo("admin_meio_dia");

		Nota($"  --     janela={GetViewport().GetVisibleRect().Size}  "
		   + $"trilho de Ki (powerupcap) = {C?.Sheet.TrilhoDeKi:0.###}");
		_espera = 1.0;
	}

	/// <summary>
	/// O QUE O OLHO VAI JULGAR, dito ANTES de qualquer foto sair.
	///
	/// Nao e checagem: e o enunciado. Escrever a expectativa depois do resultado deixa qualquer foto
	/// ser lida como confirmacao do que quer que ela mostre -- ver `RoboDeOlhada.OQueOOlhoVaiJulgar`.
	/// </summary>
	private static void OQueOOlhoVaiJulgar()
	{
		Nota("  --     1. A FOTO 1 tem que mostrar a barra de KI com o numero ACIMA de 100% e o trilho");
		Nota("  --        com FOLGA -- se o desenho encostar na ponta direita, o teto nao virou trilho.");
		Nota("  --     2. A FOTO 2 e a queixa inteira: HUD e menu P no MESMO quadro. Os dois numeros de");
		Nota("  --        Ki tem que ser o mesmo. Se a HUD disser 100% e o menu 118%, o bug esta vivo.");
		Nota("  --     3. AS QUATRO BARRAS: VIDA, KI, VIGOR e NUTRICAO, nesta ordem. A quarta e nova.");
		Nota("  --     4. A % ao lado do BP nao pode mudar entre a foto 3 (base) e a 4 (SSJ1) -- e o");
		Nota("  --        multiplicador da aba Forms tem que pular de 1x pra dezenas de x entre elas.");
		Nota("  --     5. A FOTO 5 e a prova contraria: Ki caido, % caida junto. Se ela nao cair, a");
		Nota("  --        linha esta congelada e as fotos 3 e 4 nao provavam nada.");
		Nota("");
		_ = 0;
	}

	/// <summary>
	/// ============================ A AMOSTRA DE REFERENCIA E ESTA, E NAO A CARREGADA ============================
	/// A primeira versao usava a amostra da aba Forms (com o Ki no excedente) como base da comparacao,
	/// e ela e a UNICA que nao serve: acima de 100% de Ki a `Inteireza` bate no teto (o `Math.Max(...,
	/// expressedBP)` do `peakexBP`), entao o quociente `inteireza/razaoKi` sai 1,000 por CLAMP e nao
	/// por medida. Comparado com ele, o 0,980 legitimo da forma parecia um desvio de 2%.
	///
	/// Em repouso o Ki esta em 100% e nada esta aparado: e daqui que sai o produto dos fatores parados
	/// (idade, gravidade) contra o qual todo o resto da rodada e cobrado.
	/// ========================================================================================================
	/// </summary>
	private void Repouso()
	{
		_naBase = Ler("0-repouso");
		Fotografar("0-repouso");
		_espera = 0.3;
	}

	// =====================================================================
	// 2. O KI ACIMA DE 100% -- pela TECLA C, e nao por escrita
	// =====================================================================
	/// <summary>
	/// SEGURA O C PELO CANAL DO JOGO (`SendCarregar`), que e o mesmo que o `LocalPlayer` usa quando o
	/// dono aperta a tecla. Escrever `Ki` no servidor daria o mesmo numero na barra e nao exercitaria
	/// nada do caminho que o dono percorre -- e o pedido dele e sobre o caminho dele.
	/// </summary>
	private void ComecarACarga()
	{
		C?.SendCarregar(true);
		_carregando = true;
		Nota("  --     segurando C (canal `SendCarregar`) ate o Ki passar de 100%");
		_espera = 1.0;
	}

	private double _esperandoHa;

	private void EsperarOKiPassarDe100()
	{
		_esperandoHa += 1.0;
		double r = C?.Sheet.RazaoDeKi ?? 0;

		if (r < 1.10 && _esperandoHa < 60)
		{
			_passo = 8;          // fica neste passo
			_espera = 1.0;
			return;
		}

		Conferir(r >= 1.10,
				 $"a tecla C levou o Ki acima de 100% ({r * 100:0.0}%) -- sem isto nao ha o que fotografar");
		_espera = 0.4;
	}

	private void FotoDoKiAcimaDe100()
	{
		Amostra a = Ler("1-ki-acima-de-100");

		// ============================ A AFIRMACAO QUE FALTAVA: O QUE A BARRA ESCREVEU ============================
		// Nao `Sheet.Ki / Sheet.MaxKi`. O corte morava DEPOIS disso, dentro do widget -- e era por ler
		// a ficha que a bancada anterior passava verde com o defeito na tela.
		// ======================================================================================================
		Conferir(!double.IsNaN(a.TextoDaBarra) && a.TextoDaBarra > 1.0,
				 $"a BARRA DO HUD escreveu o excedente ({a.TextoDaBarra * 100:0.0}%) -- e o texto "
			   + "desenhado, nao a ficha");

		Conferir(Math.Abs(a.TextoDaBarra - a.RazaoKi) < 0.005,
				 $"e ele e o MESMO numero da ficha ({a.RazaoKi * 100:0.0}%) -- uma fonte so");

		// O DESENHO TAMBEM: um numero certo com o trilho no talo seria meia correcao, e e justamente a
		// metade que o dono ve de longe sem ler o texto.
		Conferir(a.Preenchimento is > 0 and < 0.999,
				 $"e o trilho ainda tem FOLGA ({a.Preenchimento:0.000} do teto de carga) -- a barra "
			   + "nao esta no talo");

		Fotografar("1-ki-acima-de-100");
		_espera = 0.3;
	}

	// =====================================================================
	// 3. AS DUAS TELAS NO MESMO QUADRO -- a queixa inteira
	// =====================================================================
	private void AbrirOMenuNaAbaStats()
	{
		MenuJogo.Instancia!.Abrir();
		MenuJogo.Instancia!.IrPara("Stats");
		_espera = 0.8;
	}

	private void AsDuasTelasNoMesmoQuadro()
	{
		Amostra a = Ler("2-hud-e-menu-P");

		Conferir(MenuJogo.Instancia!.Visible, "o menu P esta ABERTO na foto (senao ela nao compara nada)");
		Conferir(!double.IsNaN(a.TextoDaBarra) && a.TextoDaBarra > 1.0,
				 $"e a barra do HUD continua escrevendo {a.TextoDaBarra * 100:0.0}% COM o menu aberto");

		Nota("  --     ESTA E A FOTO DO PEDIDO 1: HUD e menu P no mesmo quadro. O olho confere se os");
		Nota("  --     dois numeros de Ki sao o mesmo -- era a discordancia entre eles a queixa.");
		Fotografar("2-hud-e-menu-P");
		_espera = 0.3;
	}

	private void AbaForms()
	{
		MenuJogo.Instancia!.IrPara("Forms");
		_espera = 0.8;
	}

	private Amostra _naBase;
	private Amostra _naAbaForms;

	private void FotoDoMultiplicadorNaBase()
	{
		_naAbaForms = Ler("3-forms-na-base");

		// ============================ NAO SE COBRA "1x NA BASE" -- E ERA ISSO QUE EU ESPERAVA ============================
		// A primeira rodada desta bancada REPROVOU aqui, e a razao reprovada era a minha: na base, em
		// repouso, este corpo ja lia 1,11x, e com o Ki carregado a 124% subiu pra 1,374x. Os dois numeros
		// estao CERTOS -- "base" quer dizer "sem forma", nao "sem nada". O excedente de Ki e multiplicador
		// (a `powerlevel` faz `statusBuff` passar de 1), e a gravidade dominada soma de bonus na base.
		//
		// A checagem honesta e a que separa forma de resto: **o total na base tem que ser MODESTO** (a
		// ordem de grandeza de um corpo sem transformacao), e o salto de verdade e cobrado no passo da
		// forma. Um teto de 3x reprova qualquer forma vazando pra ca sem reprovar o corpo real.
		// ===========================================================================================================
		Conferir(_naAbaForms.MultTotal is > 0.9 and < 3.0,
				 $"na BASE o multiplicador total e modesto ({_naAbaForms.MultTotal:0.###}x) -- e ele NAO e "
			   + $"1,000x porque o Ki a {_naAbaForms.RazaoKi * 100:0.0}% ja e multiplicador");

		Fotografar("3-forms-na-base");

		// SOLTA O C AQUI, e nao antes: as fotos 1, 2 e 3 tinham que sair com o Ki no excedente.
		C?.SendCarregar(false);
		_carregando = false;
		_espera = 0.5;
	}

	// =====================================================================
	// 4. A INVARIANCIA -- transformar NAO pode mexer na %
	// =====================================================================
	private void Transformar()
	{
		// PELO VERB DE ADMIN, que e o caminho de producao com cinematica e tudo. Pintar a forma no
		// node daria o boneco certo e o BP de antes -- e o assunto aqui e justamente o BP.
		C?.SendVerbo("admin_forma", "ssj1");
		Nota("  --     `admin_forma ssj1` mandado -- a cinematica de estreia leva ~22 s");
		_espera = 3.0;
	}

	private double _esperandoAForma;

	private void EsperarAForma()
	{
		_esperandoAForma += 3.0;

		if ((C?.Sheet.MultTotal ?? 1) < 2.5 && _esperandoAForma < 75)
		{
			_passo = 15;
			_espera = 3.0;
			return;
		}

		// ============================ E AQUI **NAO** SE DOMINA A FORMA -- FOI O QUE MATOU A RODADA 3 ============================
		// A versao anterior mandava `admin_dominar` neste ponto, pra a forma nao drenar Ki durante a
		// foto da invariancia. So que `Formas.cs:4899` diz, escrito: *"o SSJ1 ZERA o dreno aos 100% de
		// maestria -- dominar a forma e poder ANDAR nela"*. Ou seja o passo seguinte, que espera o Ki
		// CAIR pelo dreno, ficou esperando um dreno que eu mesmo tinha acabado de desligar. Girou os
		// 120 s do prazo e a rodada morreu sem a prova contraria.
		//
		// Sem dominar, a forma crua drena de verdade e as duas provas saem da MESMA transformacao: a
		// foto 4 (a % nao viu a forma) e a foto 5 (a % desce quando o Ki desce).
		// ================================================================================================================
		_espera = 1.0;
	}

	private void FotoDaFormaVestida()
	{
		Amostra a = Ler("4-ssj1-transformado");

		// O SALTO SE COBRA CONTRA A AMOSTRA ANTERIOR, e nao contra um numero cravado. A rodada 3
		// reprovou por eu ter escrito "> 10x" de cabeca: o SSJ1 cru vale 2x e a leitura deu 6,301x
		// contra 1,361x da base -- salto de 4,6 vezes, exatamente o que se queria ver. Numero cravado
		// numa bancada de escada de transformacao envelhece na primeira mexida de balanco.
		Conferir(a.MultTotal > _naAbaForms.MultTotal * 1.8,
				 $"o multiplicador total PULOU com a forma ({_naAbaForms.MultTotal:0.###}x -> "
			   + $"{a.MultTotal:0.###}x)");

		// ============================ A DEFINICAO DO DONO, COBRADA COMO ELE A ESCREVEU ============================
		// *"ela so mudaria caso a stamina ou ki caissem, peso, gravidade etc"*. Com vida cheia e sem
		// peso, os fatores de condicao que sobram sao o Ki e os que NAO se mexem durante a rodada
		// (idade, gravidade). Entao a grandeza invariante e o QUOCIENTE `inteireza / min(1, razaoKi)`:
		// ele vale o produto dos fatores parados, e tem que dar o MESMO numero na base e na forma.
		//
		// Cobrar o quociente, e nao "antes == depois", e o que faz a checagem valer nos dois sentidos:
		// ela reprova tanto uma linha que se mexesse ao transformar quanto uma linha CONGELADA, que
		// passaria de bandeja em qualquer comparacao simples de igualdade.
		//
		// NA PRIMEIRA RODADA este quociente deu 0,98 na base -- e nao 1,00. Nao e folga de tolerancia:
		// e o `AgeDiv`, que e fator de condicao de verdade e entra na conta como manda a definicao.
		// =====================================================================================================
		double qBase = _naBase.Inteireza / Math.Max(Math.Min(1, _naBase.RazaoKi), 1e-9);
		double qForma = a.Inteireza / Math.Max(Math.Min(1, a.RazaoKi), 1e-9);

		Conferir(Math.Abs(qForma - qBase) < 0.03,
				 $"e a % efetivo NAO viu a forma: `inteireza/razaoKi` era {qBase:0.000} na base e e "
			   + $"{qForma:0.000} transformado -- a forma multiplicou os DOIS lados da fracao");

		Fotografar("4-ssj1-transformado");
		_espera = 0.3;
	}

	// =====================================================================
	// 5. A PROVA CONTRARIA -- o Ki cai, a % cai junto
	// =====================================================================
	private double _esperandoODreno;

	private void EsperarOKiCair()
	{
		_esperandoODreno += 2.0;
		double r = C?.Sheet.RazaoDeKi ?? 1;

		// ============================ O ALVO SAI DA MEDIDA, E NAO DO PALPITE ============================
		// Era 80% com 90 s de prazo, e reprovou parando em 89,3% -- a aba desta mesma rodada mostra por
		// que: *"Dreno de Ki: 0,8% do Ki por segundo"*. Saindo de 125%, 90 s de dreno chegam a ~89%, e
		// nao a 80%. O alvo agora e 92%, que a 0,8%/s cai bem dentro do prazo e ainda assim e uma queda
		// de mais de trinta pontos em cima do Ki carregado -- de sobra pra a % efetivo ter que segui-la.
		// ============================================================================================
		if (r > 0.92 && _esperandoODreno < 90)
		{
			_passo = 17;
			_espera = 2.0;
			return;
		}

		Conferir(r <= 0.92,
				 $"o dreno da forma derrubou o Ki sozinho ({r * 100:0.0}%) -- caminho do jogo, sem "
			   + "escrever campo nenhum");
		_espera = 0.4;
	}

	private void FotoDoKiCaido()
	{
		Amostra a = Ler("5-ki-caido");

		Conferir(a.Inteireza < _naBase.Inteireza - 0.05,
				 $"a % efetivo CAIU com o Ki ({_naBase.Inteireza * 100:0.0}% -> {a.Inteireza * 100:0.0}%)");

		double qBase = _naBase.Inteireza / Math.Max(Math.Min(1, _naBase.RazaoKi), 1e-9);
		double qAgora = a.Inteireza / Math.Max(Math.Min(1, a.RazaoKi), 1e-9);

		Conferir(Math.Abs(qAgora - qBase) < 0.03,
				 $"e ela caiu na MEDIDA do Ki, e nao por outro motivo: `inteireza/razaoKi` continua "
			   + $"{qAgora:0.000} contra os {qBase:0.000} da base");

		// ============================ CONTRA O REPOUSO, E NAO CONTRA A AMOSTRA CARREGADA ============================
		// Esta checagem tambem reprovou na rodada boa, e a licao e a mesma da familia: **o total inclui
		// o Ki**. Na aba Forms o total lia 2,77x com o Ki a 125%; com o Ki caido a 89% ele leu 1,98x --
		// 2,77 x (0,89/1,25) = 1,97, ou seja a queda esta CERTA e e a mesma queda da % efetivo.
		// Comparar contra aquele 1,374x inflado pelo Ki era cobrar que a forma tapasse o buraco do Ki.
		//
		// O que se quer afirmar aqui e outra coisa: **a forma continua de pe embaixo da queda**. Isso se
		// le contra o REPOUSO (1,11x, Ki em 100%), que e o mesmo corpo sem forma e sem excedente.
		// =========================================================================================================
		Conferir(a.MultTotal > _naBase.MultTotal * 1.4,
				 $"enquanto o multiplicador da FORMA ficou de pe ({a.MultTotal:0.###}x contra os "
			   + $"{_naBase.MultTotal:0.###}x do mesmo corpo em repouso e sem forma) -- sao dois "
			   + "numeros independentes, e e essa a historia que a aba conta");

		Fotografar("5-ki-caido");
		_espera = 0.3;
	}

	// =====================================================================
	// 6. A BARRA DE NUTRICAO -- ela SE MEXE?
	// =====================================================================
	/// <summary>
	/// UMA BARRA PARADA EM 100% E UMA BARRA CORRETA DESENHAM O MESMO PIXEL. Fotografar a nutricao
	/// cheia provaria que ela existe e nada sobre ela estar ligada no dado -- que e exatamente o cego
	/// que deixou o corte de Ki viver quatro mil checagens verdes. Ver `GameServer.EstomagoDeTeste`
	/// pra por que a bancada empurra o tanque em vez de esperar a digestao (medido: ~0,2% por minuto).
	/// </summary>
	private void EsvaziarOEstomago()
	{
		int id = C?.LocalId ?? 0;
		bool foi = Jandirus.Server.GameServer.Instance?.EstomagoDeTeste(id, 15) ?? false;
		Conferir(foi, "a bancada alcancou o estomago deste corpo no servidor");
		_espera = 1.2;
	}

	private void FotoDaNutricaoBaixa()
	{
		Amostra a = Ler("6-nutricao-baixa");

		Conferir(a.RazaoNutricao is > 0.20 and < 0.40,
				 $"a NUTRICAO chegou ao cliente pelo caminho de producao ({a.RazaoNutricao * 100:0.0}%)");

		Nota("  --     o olho confere na foto: a quarta barra (NUTRICAO) esta pela metade, e nao cheia.");
		Fotografar("6-nutricao-baixa");
		_espera = 0.3;
	}

	private void EncherOEstomago()
	{
		Jandirus.Server.GameServer.Instance?.EstomagoDeTeste(C?.LocalId ?? 0, 50);
		_espera = 1.2;
	}

	private void FotoDaNutricaoCheia()
	{
		Amostra a = Ler("7-nutricao-cheia");

		Conferir(a.RazaoNutricao > 0.95,
				 $"e ela VOLTOU quando o tanque encheu ({a.RazaoNutricao * 100:0.0}%) -- barra ligada "
			   + "nos dois sentidos, e nao um numero que so sabe descer");

		Fotografar("7-nutricao-cheia");
		_espera = 0.3;
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// DUAS IMAGENS POR PASSO, e as duas servem:
	///
	///   * O QUADRO INTEIRO -- e a unica que mostra HUD e menu P juntos, que e o pedido 1.
	///   * O CANTO ESQUERDO AMPLIADO -- as quatro barras tem 12 px de altura cada; num quadro de
	///     1152 px de largura ninguem le "118%" nem distingue o segmento de sobrecarga do trilho.
	///
	/// O RECORTE SAI DO NODE, e nao de um retangulo cravado: a HUD e ancorada e o painel cresce com o
	/// conteudo (foi assim que o painel da direita ja empurrou o boneco pra fora da tela). Cravar
	/// pixel daria enquadramentos diferentes em cada resolucao e a rodada do dono nao se compararia
	/// com a minha.
	/// </summary>
	private void Fotografar(string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Nota($"  --     [{rotulo}] SEM FOTO -- viewport vazio (headless nao renderiza). "
			   + "Suba com janela.");
			return;
		}
		img.Convert(Image.Format.Rgba8);

		string cheia = ProjectSettings.GlobalizePath($"user://mostrador-{rotulo}.png");
		img.SavePng(cheia);

		if (RecorteDaHud(img) is { } canto)
			Ampliar(canto, 3).SavePng(ProjectSettings.GlobalizePath($"user://mostrador-{rotulo}-HUD.png"));

		Nota($"  ok     foto {cheia} (+ o canto da HUD ampliado 3x)");
	}

	/// <summary>
	/// O CANTO ESQUERDO, achado pela BARRA DE KI e nao por coordenada. A barra vive dentro da coluna
	/// da HUD; subindo dois pais chega-se ao painel inteiro (nome, as quatro barras, a linha do BP com
	/// a % efetivo ao lado) -- que e exatamente o conjunto que esta rodada mexeu.
	/// </summary>
	private static Image? RecorteDaHud(Image quadro)
	{
		if (Hud.Instancia?.BarraDeKi.GetParent() is not Control coluna) return null;

		Rect2 r = coluna.GetGlobalRect().Grow(16);
		int x = Mathf.Clamp((int)r.Position.X, 0, quadro.GetWidth() - 1);
		int y = Mathf.Clamp((int)r.Position.Y, 0, quadro.GetHeight() - 1);
		int w = Mathf.Clamp((int)r.Size.X, 1, quadro.GetWidth() - x);
		int h = Mathf.Clamp((int)r.Size.Y, 1, quadro.GetHeight() - y);

		return quadro.GetRegion(new Rect2I(x, y, w, h));
	}

	/// <summary>
	/// VIZINHO-MAIS-PROXIMO, e nao bilinear -- mesma razao do `RoboDeOlhada.Ampliar`: interpolacao
	/// suave inventa cor entre dois pixels vizinhos, e aqui a pergunta e "que cor tem o segmento
	/// acima do traco dos 100%".
	/// </summary>
	private static Image Ampliar(Image src, int vezes)
	{
		Image copia = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, src.GetFormat());
		copia.BlitRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), Vector2I.Zero);
		copia.Resize(src.GetWidth() * vezes, src.GetHeight() * vezes, Image.Interpolation.Nearest);
		return copia;
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void Relatorio()
	{
		_acabou = true;
		if (_carregando) C?.SendCarregar(false);
		MenuJogo.Instancia?.Fechar();

		Nota("");
		Nota("=================== A TABELA, PRA LER AS FOTOS ===================");
		Nota("rotulo                  Ki(ficha)  Ki(BARRA)  trilho  efetivo   mult   nutricao");
		foreach (Amostra a in _amostras)
			Nota($"{a.Rotulo,-22}  {a.RazaoKi * 100,7:0.0}%  {a.TextoDaBarra * 100,8:0.0}%  "
			   + $"{a.Preenchimento,6:0.000}  {a.Inteireza * 100,6:0.0}%  {a.MultTotal,6:0.##}x  "
			   + $"{a.RazaoNutricao * 100,7:0.0}%");
		Nota("=================================================================");
		Nota($"{_oks} ok / {_falhas.Count} falhas");
		foreach (string f in _falhas) Nota("  FALHA  " + f);
		Nota("As fotos estao em: " + ProjectSettings.GlobalizePath("user://"));
	}
}
