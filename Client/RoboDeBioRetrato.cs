using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// ============================ A FOTO DA ESCADA DO BIO-ANDROIDE (`--diagbio`) ============================
/// Um retrato por degrau, lado a lado. Ela sobe junto do `--biovivo` do servidor
/// (<see cref="Jandirus.Server.GameServer"/>, `GameServer.BioPalco.cs`), que faz um corpo nascer a seis
/// tiles do host e subir a escada inteira pelas portas de producao.
///
/// ============================ POR QUE NUMERO NAO RESPONDE ISTO ============================
/// O `--bioteste` tem 159 provas e **todas passariam verdes** num servidor que subisse a escada sem que
/// um pixel mudasse: `bio_stage` certo na ficha, `Appearance.Corpo` certo no pacote, e os quatro
/// degraus desenhando o mesmo boneco. O caminho do desenho e outro -- `VisualCatalog.CorpoSprite` ->
/// `CharacterVisual.Vestir` -> `Trocar` --, e a arte dos quatro degraus passou meses importada **sem um
/// unico consumidor em C#**. Uma lista de caminhos que ninguem carrega e exatamente uma escada em que
/// todos os degraus saem iguais na tela.
///
/// Este projeto ja perdeu tres coisas assim (a cor do desvio, a barra de vida, o rastro da agua), e as
/// tres tinham bancada verde.
///
/// ============================ E A FOTO SOZINHA TAMBEM NAO RESPONDE ============================
/// Sete arquivos PNG num diretorio nao provam nada: quem olha ve sete bonecos verdes e acredita. Entao
/// alem de fotografar, ela **mede**:
///
///   * a FOLHA que cada pose vestiu, lida do material (`FolhaDoCorpoDeTeste`), conferida contra a
///     lista do Core (`BioAndroids.Corpos`) -- medida ABSOLUTA, e nao "mudou alguma coisa";
///   * a DIFERENCA DE PIXEL entre cada par de retratos, em porcentagem. Dois degraus que saiam iguais
///     na tela viram um numero perto de zero e uma linha vermelha, mesmo que a folha esteja certa;
///   * e a tira `bio-escada.png`, com os sete lado a lado -- que e o que o dono vai olhar.
///
/// PRECISA DE JANELA. No headless o `GetImage` volta vazio e nao ha veredito -- e ela DIZ isso em vez
/// de fechar com "0 falhas", que e o modo de falha mais perigoso de uma bancada visual.
/// ======================================================================================================
/// </summary>
public partial class RoboDeBioRetrato : Node
{
	/// <summary>Onde as fotos saem. Duas rodadas escrevem por cima -- de proposito.</summary>
	private const string Pasta = "user://";

	/// <summary>
	/// Quanto o corpo tem que ficar PARADO num estado antes de virar retrato.
	///
	/// A troca de folha chega num pacote e o corpo leva quadros pra vestir (e a forma roda cena).
	/// Fotografar no instante do pacote fotografa a TRANSICAO -- o erro classico deste tipo de
	/// bancada, ja pago uma vez no `RoboDeDoisCorpos`.
	/// </summary>
	private const double SegundosPraAssentar = 2.0;

	/// <summary>Quantas poses a escada tem. Ver o cabecalho do `GameServer.BioPalco.cs`.</summary>
	private const int Poses = 8;

	/// <summary>
	/// ============================ O RECORTE, E ELE FOI CALIBRADO NA FOTO E NAO NO CHUTE ============================
	/// A primeira rodada saiu com o boneco na QUINA de baixo e metade dele fora do quadro, porque a
	/// caixa fora escrita achando que `PosicaoDesenhadaDe` devolve os PES. Ela devolve o CENTRO do
	/// sprite -- da pra ler isso na propria foto: a cabeca comecava a 55 px do topo de um corte de 112
	/// e o corpo saia por baixo. Por isso a caixa e CENTRADA no ponto.
	///
	/// E ela e larga o bastante pra caber a AURA: a pose 6 e a unica em que o corpo nao troca de folha
	/// (o bio nao ganha cabelo em forma nenhuma), entao tudo o que ela tem pra mostrar sao aura e
	/// raios. Um recorte justo no boneco reprovaria justamente a pose que mede o DNA Saiyajin.
	/// ==========================================================================================================
	/// </summary>
	private const int LarguraDoCorte = 76, AlturaDoCorte = 96;

	/// <summary>Quantas vezes ampliar o recorte. NEAREST: o que se julga aqui e PIXEL de sprite.</summary>
	private const int Ampliacao = 4;

	/// <summary>
	/// ============================ QUANTO DOIS PIXELS PRECISAM DIFERIR, E POR QUE NAO E QUASE ZERO ============================
	/// Ela era 0,08 (somando os tres canais), e com esse valor a bancada aprovou como "diferentes" duas
	/// fotos que sao o MESMO boneco parado: entre uma pose e outra passam oito segundos, a luz do dia
	/// anda, e a grama inteira muda de tom por um fio. Um terco de todos os pixels do recorte e mato.
	///
	/// 0,30 e calibrado contra um par CONHECIDO: as poses 4 e 6 sao, por regra do jogo, a mesma imagem
	/// (o bio nao ganha cabelo, o SSJ1 nao tem raios, e a aura so acende acima de 100% de Ki). Com este
	/// valor aquele par cai perto de zero e os pares de sprite diferente continuam acima de 15%. A
	/// bancada tem os dois lados medidos, entao a calibragem nao e chute.
	/// ==================================================================================================================
	/// </summary>
	private const float ToleranciaDoPixel = 0.30f;

	/// <summary>
	/// O piso da diferenca entre dois retratos consecutivos, em fracao de pixels do recorte.
	///
	/// TRES POR CENTO, e o numero tem razao: o boneco ocupa cerca de um sexto do recorte, e a menor
	/// mudanca esperada da escada (o Super Saiyajin, em que o CORPO nao troca -- so entram aura e
	/// raios) ainda pinta bem mais que isso. Um piso alto demais reprovaria justamente a pose que
	/// mede o DNA; um piso zero aceitaria dois degraus identicos.
	/// </summary>
	private const double PisoDaDiferenca = 0.03;

	private int _idDoPalco;
	private string _nomeVisto = "", _racaVista = "";
	private double _relogio, _mudouEm;
	private string _carimbo = "", _carimboPendente = "";
	private bool _pendente, _fechou;

	/// <summary>Segundos totais antes de fechar sozinha, mesmo com poses faltando.</summary>
	public double Fim = 220;

	/// <summary>
	/// ANTES DISTO NAO SE FOTOGRAFA NADA. A primeira rodada mediu 21% de "ruido" na pose 0 e zero em
	/// todas as outras: nao era mato, era a CAMERA e a zona ainda assentando logo depois do login. Um
	/// retrato de controle tirado no meio do carregamento envenena todas as comparacoes que dependem
	/// dele -- e a pose 0 e justamente a que diz "a criatura deixou de parecer gente".
	/// </summary>
	private const double SegundosAntesDeComecar = 5;

	/// <summary>
	/// ============================ O SEGUNDO CLIQUE E O CONTROLE DE RUIDO, E ELE NASCEU DE UM FALSO VERDE ============================
	/// A primeira rodada boa desta bancada aprovou a pose 6 (SUPER SAIYAJIN) contra a 4 (PERFEITA) com
	/// **21% de pixels diferentes** -- e as duas fotos, olhadas lado a lado, sao o MESMO boneco parado.
	/// Os 21% eram o MATO: a grama do berco e animada e a luz do dia anda, e entre uma pose e outra
	/// passam oito segundos. Ou seja, a medida de pixel estava aprovando o fundo.
	///
	/// E esse e o defeito que esta bancada inteira existe pra pegar, acontecendo DENTRO dela. Foi achado
	/// OLHANDO a tira -- que e o motivo de a tira existir.
	///
	/// O CONSERTO E UM SEGUNDO CLIQUE DA MESMA POSE, alguns segundos depois: a diferenca entre os dois
	/// e o ruido do CENARIO, medido no proprio quadro, sem constante escrita. Uma pose so conta como
	/// diferente da outra quando ela supera o proprio ruido com folga.
	/// ==========================================================================================================================
	/// </summary>
	private sealed class Retrato
	{
		public int Pose;
		public string Folha = "", FolhaDaForma = "", Raca = "";
		public Image? Corte;

		/// <summary>A MESMA pose, alguns segundos depois. Ver o cabecalho.</summary>
		public Image? Bis;

		/// <summary>Quanto o CENARIO se mexeu sozinho entre os dois cliques. -1 = nao medido.</summary>
		public double Ruido = -1;

		/// <summary>O que os nodes de efeito diziam no momento do clique.</summary>
		public bool AuraAcesa, RaiosEmitindo;
		public string CorDaAura = "";

		/// <summary>
		/// A CAMADA DE OLHOS ESTAVA DESENHADA? -- e ela responde ao pedido literal do dono: *"os bio
		/// androides o OLHO FICA VOANDO na forma de BARATA, faca com q os olhos SUMAM ao ficar nessa
		/// forma"*, com foto anexada das duas pupilas flutuando acima do casco da larva.
		///
		/// **A DIFERENCA DE PIXEL NAO PEGARIA ISSO.** Os olhos sao dois pontos brancos de poucos pixels
		/// num recorte de 76x96 ampliado -- eles ficam MUITO abaixo do piso de 3% que separa um degrau
		/// do outro, e a pose 1 ja e aprovada com folga por trocar o corpo inteiro. Ou seja: o defeito
		/// que o dono viu na tela e invisivel pra toda a maquinaria de medida que esta bancada tem.
		/// Por isso ele e uma pergunta propria, feita ao node.
		/// </summary>
		public bool OlhosVisiveis;

		/// <summary>
		/// ============================ O CONTORNO E O QUE OS **OUTROS** VEEM DA CARGA ============================
		/// A bancada cobrava `Aura.AcesaDeTeste` na pose 7 e ficava vermelha com o jogo certo. O caminho
		/// do brilho num corpo ALHEIO nao passa pelo `Acender`: o `aura_ki` (`MandarEfeito`) so vai pro
		/// `Peer` do proprio dono, e o palco nao tem dono. Quem viaja pra todo mundo e o BIT de
		/// sobrecarga, dentro do snapshot (`EntityState.Sobrecarregado`), e do lado do cliente ele acende
		/// o CONTORNO (`World.MarcarSobrecarga` -> `AplicarContorno`).
		///
		/// Ou seja, a pergunta "da pra ver que ele esta acima do limite?" se responde no contorno -- e
		/// medi-la na aura media um canal que, por desenho, so existe na tela do proprio jogador.
		/// ====================================================================================================
		/// </summary>
		public float Contorno;
	}

	/// <summary>Segundos entre o clique e o segundo clique da mesma pose. Ver <see cref="Retrato"/>.</summary>
	private const double SegundosAteOBis = 3.5;

	private double _bisEm;

	private readonly List<Retrato> _retratos = [];
	private readonly List<string> _linhas = [];
	private int _oks;
	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		GD.Print("[bio-foto] " + (ok ? "  ok   " : "  FALHA") + "  " + oque);
	}

	private void Anotar(string s) { _linhas.Add(s); GD.Print("[bio-foto] " + s); }

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;

		// ============================ O `PeerLook` E COMO ELA ACHA O CORPO CERTO ============================
		// A Terra tem cento e quarenta e oito cidadaos. "O corpo mais perto" daria a resposta certa
		// quase sempre, e numa bancada visual "quase sempre" e uma foto de um transeunte com nome de
		// prova. O servidor manda um `PeerLook` com id + nome pra zona inteira a cada degrau, e o nome
		// do palco e uma constante compartilhada -- nao ha string digitada duas vezes.
		// ================================================================================================
		cli.PeerLooked += AoReceberFicha;

		// ============================ QUEM DIZ "A POSE MUDOU" E O PALCO, E NAO O SPRITE ============================
		// O disparo era por conta propria: o robo via a folha do corpo trocar e fotografava. Ele perdia
		// TRES das oito poses, sempre as mesmas -- a 6 (Super Saiyajin parado) veste as MESMAS folhas da
		// 4 (o bio nao ganha cabelo em forma nenhuma), e a 5 (Super Perfeita) tem raios cujo bit de
		// emissao pisca, entao o corpo nunca ficava "parado" o bastante.
		//
		// Perceber isso e o achado: **uma bancada que so olha o sprite nao consegue ver uma pose em que
		// o sprite nao muda** -- e essa pose e justamente a que o dono pediu ("virar SUPER SAIYAJIN").
		// Agora quem avisa e quem executou, pelo canal de chat que o jogo ja tem.
		// ========================================================================================================
		cli.Falou += AoOuvir;
		GD.Print($"[bio-foto] no ar -- procurando o corpo '{Jandirus.Server.GameServer.NomeDoPalcoDoBio}'");
	}

	private void AoReceberFicha(int quem, string nome, string raca, string genero,
								Jandirus.Core.Appearance.Appearance ap)
	{
		// O NOME MUDA NO NASCIMENTO -- a criatura passa a se chamar "Bio-Androide de ...". Por isso o
		// crivo do nome so vale ate achar o corpo; dali em diante quem manda e o ID. Sem isto o
		// `_racaVista` ficava congelado em "Human" na escada inteira, e o carimbo do fotografo mentia
		// sobre metade do que ele estava vendo.
		if (_idDoPalco == 0)
		{
			if (nome != Jandirus.Server.GameServer.NomeDoPalcoDoBio) return;
			Anotar($"achei o palco: id {quem}");
			_idDoPalco = quem;
		}
		else if (quem != _idDoPalco) return;

		_nomeVisto = nome;
		_racaVista = raca;
	}

	/// <summary>Qual pose o palco anunciou por ultimo, e quando. -1 = nenhuma ainda.</summary>
	private int _poseAnunciada = -1;

	private void AoOuvir(Jandirus.Net.Protocol.Fala tipo, string quem, string texto)
	{
		string marca = Jandirus.Server.GameServer.MarcaDoPalcoDoBio;
		int i = texto.IndexOf(marca, StringComparison.Ordinal);
		if (i < 0 || !int.TryParse(texto[(i + marca.Length)..].Trim(), out int pose)) return;
		if (pose == _poseAnunciada) return;

		_poseAnunciada = pose;
		_mudouEm = _relogio;
		_pendente = true;
		Anotar($"o palco anunciou a pose {pose}");
	}

	public override void _Process(double delta)
	{
		if (_fechou) return;
		_relogio += delta;

		if (_idDoPalco != 0 && _relogio >= SegundosAntesDeComecar) Vigiar();

		if (_relogio >= Fim || _retratos.Count >= Poses && _relogio - _mudouEm > SegundosPraAssentar * 2)
			Fechar();
	}

	/// <summary>
	/// O ESTADO SE LE DO **MATERIAL**, e nao do pacote. O carimbo e a folha do corpo base mais a da
	/// camada de forma: sao as duas coisas que a escada troca, e as duas moram no sprite vestido.
	/// </summary>
	private void Vigiar()
	{
		if (World.Instancia?.CorpoDeTeste(_idDoPalco) is not { } corpo) return;
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;

		string folha = vis.FolhaDoCorpoDeTeste;
		string forma = vis.FolhaDoCorpoDaFormaDeTeste;

		// A AURA E OS RAIOS ENTRAM NO CARIMBO, e sem isso a pose 7 nao existiria: ela veste as MESMAS
		// duas folhas da pose 6 (o bio nao ganha cabelo em forma nenhuma) e a unica coisa que muda nela
		// e a aura acendendo. Um carimbo so de folha nao veria a transformacao aparecer.
		Node2D? oCorpo = World.Instancia?.CorpoDeTeste(_idDoPalco);
		bool auraAgora = oCorpo?.GetNodeOrNull<Aura>("Aura")?.AcesaDeTeste ?? false;
		bool raiosAgora = oCorpo?.GetNodeOrNull<RaiosDaForma>("Raios")?.EmitindoDeTeste ?? false;

		string agora = $"{folha}|{forma}|{_racaVista}|aura{auraAgora}|raios{raiosAgora}";

		// O CARIMBO DEIXOU DE SER O GATILHO e virou apenas o que se ANOTA -- ver o bloco do `AoOuvir`.
		// Ele continua no relatorio porque e a prova de que o corpo mudou de folha; o que ele nao pode
		// mais fazer e decidir a hora da foto, porque ha pose em que ele nao muda.
		_carimboPendente = agora;

		// O SEGUNDO CLIQUE DA MESMA POSE -- ver o cabecalho de `Retrato`. Ele nao troca a foto que vai
		// pro disco: ele so mede o quanto o CENARIO se mexe sozinho no intervalo.
		if (!_pendente && _bisEm > 0 && _relogio >= _bisEm && _retratos.Count > 0)
		{
			_bisEm = 0;
			Retrato ultimo = _retratos[^1];
			ultimo.Bis = Recortar();
			if (ultimo.Corte != null && ultimo.Bis != null)
			{
				ultimo.Ruido = Diferenca(ultimo.Corte, ultimo.Bis);
				Anotar($"     ruido do cenario na pose {ultimo.Pose}: {ultimo.Ruido * 100:0.0}% "
					 + $"(a MESMA pose, {SegundosAteOBis:0.#} s depois -- e o mato e a luz andando)");
			}
		}

		// ============================ NAO SE FOTOGRAFA UM CORPO NO MEIO DA PROPRIA CINEMATICA ============================
		// Dois dos degraus do bio rodam cena de 28,0 s (`Cinematicas.BioEvoluindo`), e o cliente
		// **segura a aparencia nova ate a virada da cena** de proposito (`World.AoReceberAparencia`:
		// vestir no meio penduraria a silhueta de luz sobre um corpo que ja e o novo -- duas silhuetas
		// de tamanhos diferentes empilhadas, que e um defeito que o dono ja fotografou).
		//
		// Sem esta linha, as poses 3 e 4 eram retratadas dois segundos depois do ANUNCIO, ou seja vinte
		// e seis segundos antes de a folha nova chegar, e saiam vestindo o degrau ANTERIOR. As duas
		// linhas vermelhas diziam a verdade sobre a foto e mentiam sobre o jogo.
		//
		// `_mudouEm = _relogio` RE-ARMA o assentamento em vez de so adiar: assim os 2,0 s passam a
		// contar do FIM da cena, e nao do anuncio -- fotografar no quadro seguinte a virada pegaria a
		// poeira baixando e o clarao de tela.
		// ============================================================================================================
		if (vis.SilhuetaDeCenaDeTeste != null) { _mudouEm = _relogio; return; }

		if (!_pendente || _relogio - _mudouEm < SegundosPraAssentar) return;

		_pendente = false;
		_carimbo = agora;
		Capturar(vis, folha, forma);
		_bisEm = _relogio + SegundosAteOBis;
	}

	private void Capturar(CharacterVisual vis, string folha, string forma)
	{
		// ============================ OS NODES DE EFEITO ENTRAM NO RETRATO ============================
		// A pose 6 (SUPER SAIYAJIN) nao troca folha nenhuma -- o bio nao ganha cabelo em forma alguma
		// (`HairObject.dm:168`) --, entao tudo o que ela tem pra mostrar sao AURA e RAIOS. Sem ler os
		// dois nodes, uma pose 6 sem efeito nenhum e indistinguivel de uma pose 4, e a bancada teria
		// que decidir isso so pela diferenca de pixel, que e a medida mais fragil que ela tem.
		// ==========================================================================================
		Node2D? corpo = World.Instancia?.CorpoDeTeste(_idDoPalco);
		Aura? aura = corpo?.GetNodeOrNull<Aura>("Aura");
		RaiosDaForma? raios = corpo?.GetNodeOrNull<RaiosDaForma>("Raios");

		var r = new Retrato
		{
			Pose = Math.Max(_poseAnunciada, 0), Folha = folha, FolhaDaForma = forma, Raca = _racaVista,
			Corte = Recortar(),
			AuraAcesa = aura?.AcesaDeTeste ?? false,
			RaiosEmitindo = raios?.EmitindoDeTeste ?? false,
			CorDaAura = aura?.CorDaLuzDeTeste.ToHtml(false) ?? "",
			Contorno = vis.ContornoNoMaterialDeTeste().Item2,
			OlhosVisiveis = vis.OlhosVisiveisDeTeste,
		};
		_retratos.Add(r);

		Anotar($"pose {r.Pose}: raca '{r.Raca}' | corpo \"{Nome(folha)}\" | forma \"{Nome(forma)}\" "
			 + $"| cabelo visivel {vis.TemCabeloVisivelDeTeste} | OLHOS {r.OlhosVisiveis} "
			 + $"| AURA acesa {r.AuraAcesa} ({r.CorDaAura}) "
			 + $"| RAIOS {r.RaiosEmitindo} | CONTORNO {r.Contorno:0.000} "
			 + $"| recorte {(r.Corte == null ? "SEM FOTO" : $"{r.Corte.GetWidth()}x{r.Corte.GetHeight()}")}");

		if (r.Corte != null)
		{
			string caminho = ProjectSettings.GlobalizePath($"{Pasta}bio-retrato-{r.Pose}.png");
			r.Corte.SavePng(caminho);
			Anotar($"     -> {caminho}");
		}
	}

	private static string Nome(string caminho) =>
		caminho.Length == 0 ? "(nenhuma)" : System.IO.Path.GetFileNameWithoutExtension(caminho);

	/// <summary>A tela inteira, recortada em volta de onde o corpo do palco esta DESENHADO.</summary>
	private Image? Recortar()
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{ Anotar("     SEM FOTO: o viewport voltou vazio (headless nao renderiza)"); return null; }
		if (World.Instancia?.PosicaoDesenhadaDe(_idDoPalco) is not { } mundo)
		{ Anotar($"     SEM FOTO: o corpo {_idDoPalco} nao tem posicao desenhada"); return null; }

		Vector2 tel = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		var caixa = new Rect2I((int)tel.X - LarguraDoCorte / 2, (int)tel.Y - AlturaDoCorte / 2,
							   LarguraDoCorte, AlturaDoCorte);
		caixa = caixa.Intersection(new Rect2I(0, 0, tela.GetWidth(), tela.GetHeight()));

		// ============================ UM RECORTE FORA DA TELA E UMA FOTO QUE NAO EXISTE ============================
		// Aconteceu numa rodada inteira: o palco caiu fora do campo da camera e as quatro poses do meio
		// sairam "SEM FOTO" com o placar acusando so o total. A mensagem diz ONDE o corpo estava, porque
		// "sem foto" sozinho manda procurar o defeito no lugar errado (o `GetImage`, a janela, o
		// headless) quando o problema e enquadramento.
		// ========================================================================================================
		if (caixa.Size.X < 16 || caixa.Size.Y < 16)
		{
			Anotar($"     SEM FOTO: o palco esta FORA DA TELA -- corpo em {tel} numa tela de "
				 + $"{tela.GetWidth()}x{tela.GetHeight()}");
			return null;
		}

		Image corte = tela.GetRegion(caixa);
		corte.Resize(corte.GetWidth() * Ampliacao, corte.GetHeight() * Ampliacao,
					 Image.Interpolation.Nearest);
		return corte;
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Fechar()
	{
		_fechou = true;
		GD.Print("\n[bio-foto] ===== A ESCADA DO BIO, EM RETRATO =====");
		foreach (string l in _linhas) GD.Print("[bio-foto] " + l);

		// ============================ UMA BANCADA VISUAL QUE NAO FOTOGRAFOU NADA REPROVA ============================
		// E o modo de falha mais perigoso desta familia: o palco nao subiu, o `GetImage` voltou vazio,
		// e o placar sai "0 falhas" -- que se le exatamente igual a "tudo certo".
		// ========================================================================================================
		Conferir(_idDoPalco != 0, $"a bancada achou o corpo do palco pelo nome "
								+ $"('{Jandirus.Server.GameServer.NomeDoPalcoDoBio}')");
		Conferir(_retratos.Count >= Poses,
				 $"as {Poses} poses da escada passaram pela tela (peguei {_retratos.Count})");
		Conferir(_retratos.All(r => r.Corte != null),
				 "e TODAS renderam foto -- no headless o `GetImage` volta vazio e nao ha veredito");

		AsFolhasContraOCore();
		ADiferencaDePixel();
		ATira();

		GD.Print(_falhas.Count == 0
			? $"[bio-foto] ===== {_oks} ok, 0 falha(s) ====="
			: $"[bio-foto] ===== {_oks} ok, {_falhas.Count} FALHA(S) =====\n[bio-foto]   "
			  + string.Join("\n[bio-foto]   ", _falhas));

		GetTree().Quit();
	}

	/// <summary>
	/// A FOLHA DE CADA POSE, CONTRA A LISTA DO CORE. Medida ABSOLUTA: "mudou alguma coisa" ficaria
	/// verde numa escada que trocasse os quatro sprites entre si.
	/// </summary>
	private void AsFolhasContraOCore()
	{
		string[] esperado =
		[
			"",                                     // 0 -- humano: qualquer corpo do catalogo
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Larva)],
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Imperfeito)],
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.SemiPerfeito)],
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito)],
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito)],   // 5 -- a Super Perfeita e CAMADA
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito)],   // 6 -- o SSJ nao troca corpo
			BioAndroids.Corpos[BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito)],   // 7 -- nem carregando
		];

		string[] rotulo =
		[
			"HUMANO (o controle)", "LARVA", "IMPERFEITO", "SEMI-PERFEITO", "FORMA PERFEITA",
			"SUPER PERFEITA", "SUPER SAIYAJIN", "SUPER SAIYAJIN CARREGANDO",
		];

		for (int i = 0; i < _retratos.Count && i < esperado.Length; i++)
		{
			Retrato r = _retratos[i];
			if (i == 0)
			{
				Conferir(!r.Folha.Contains("Bio", StringComparison.OrdinalIgnoreCase)
						 && !r.Folha.Contains("Cell", StringComparison.OrdinalIgnoreCase),
						 $"pose 0 ({rotulo[0]}): o corpo ainda e de gente -- \"{Nome(r.Folha)}\". Sem "
					   + "esta linha, 'os degraus sao diferentes' nao diz que a criatura deixou de "
					   + "parecer gente");
				continue;
			}

			Conferir(string.Equals(r.Folha, esperado[i], StringComparison.OrdinalIgnoreCase),
					 $"pose {i} ({rotulo[i]}): o corpo DESENHADO e \"{Nome(esperado[i])}\", que e o "
				   + $"que `BioAndroids.Corpos` manda -- e o que chegou foi \"{Nome(r.Folha)}\"");
		}

		// A SUPER PERFEITA E CAMADA DE FORMA, e e por isso que ela tem uma linha propria: o corpo de
		// repouso continua sendo o perfeito, e quem troca e o `CorposDeForma`. Sem esta linha, uma
		// Super Perfeita que nao vestisse nada passaria pela linha de cima (o corpo perfeito esta la).
		if (_retratos.Count > 5)
			Conferir(_retratos[5].FolhaDaForma.Contains("Bio Android 4", StringComparison.OrdinalIgnoreCase),
					 "pose 5 (SUPER PERFEITA): a CAMADA DE FORMA vestiu `Bio Android 4` -- ela nao "
				   + "troca o corpo de repouso, ela veste por cima. O que chegou foi "
				   + $"\"{Nome(_retratos[5].FolhaDaForma)}\"");

		if (_retratos.Count > 6)
			Conferir(_retratos[6].FolhaDaForma.Length == 0
					 || !_retratos[6].FolhaDaForma.Contains("Bio Android 4", StringComparison.OrdinalIgnoreCase),
					 "pose 6 (SUPER SAIYAJIN): a camada da Super Perfeita SAIU do corpo -- as duas "
				   + "escadas nao convivem, e no DM o SSJ2 sobrescrevia a Super Perfeita calado");

		// ============================ E OS EFEITOS, QUE SAO A UNICA ROUPA DAS DUAS ULTIMAS POSES ============================
		// A aura e os raios sao lidos do NODE (`Aura.AcesaDeTeste`, `RaiosDaForma.EmitindoDeTeste`), e
		// nao da forma que o pacote trouxe. As duas medidas se cobrem: o node diz que o efeito foi
		// LIGADO, a diferenca de pixel diz que ele foi DESENHADO. Sozinha, a primeira e "uniform
		// escrito"; sozinha, a segunda aprova mato.
		//
		// E A BASE ENTRA JUNTO, nos dois sentidos: um corpo que ficasse com a aura acesa na pose 0
		// (antes de ser bio) faria as duas ultimas linhas ficarem verdes sem nenhuma transformacao ter
		// acontecido.
		// ==============================================================================================================
		if (_retratos.Count > 0)
			Conferir(!_retratos[0].AuraAcesa && !_retratos[0].RaiosEmitindo,
					 "pose 0 (HUMANO): sem aura e sem raios -- o controle das duas linhas abaixo");

		// ============================ E OS OLHOS DA LARVA -- O PEDIDO DO DONO, MEDIDO ============================
		// *"os bio androides o OLHO FICA VOANDO na forma de BARATA, faca com q os olhos SUMAM ao ficar
		// nessa forma"*. A `CellLarva` e uma barata: nao ha rosto onde pendurar a camada de olhos, e
		// no port ela era INCONDICIONAL (o `visual.json` tem uma folha de olho so, global, sem tabela
		// por raca como a de careca). Quem apagava camada era so a `_criatura`, derivada da ALTURA do
		// quadro -- e a larva tem os mesmos 32x32 de todo mundo.
		//
		// SAO TRES LINHAS E NAO UMA, porque as tres quebram separadas:
		//   * a pose 0 e o CONTROLE. Sem ela, "a larva nao tem olhos" ficaria verde num port que
		//     tivesse perdido a camada de olhos inteira -- que e o defeito oposto e igualmente feio;
		//   * a pose 1 e o pedido;
		//   * as poses 2..4 sao a metade que impede o conserto de virar exagero: os tres degraus acima
		//     da larva SAO humanoides desenhados, e apagar o olho neles seria trocar um bug por outro.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque o `CorpoTemRosto` de `VisualCatalog` por um `true` e a
		// segunda linha cai; devolva o `Escondida` ao que era e cai a mesma.
		// =====================================================================================================
		if (_retratos.Count > 0)
			Conferir(_retratos[0].OlhosVisiveis,
					 "pose 0 (HUMANO): os OLHOS estao desenhados -- o controle das duas linhas abaixo");

		if (_retratos.Count > 1)
			Conferir(!_retratos[1].OlhosVisiveis,
					 "pose 1 (LARVA): os OLHOS SUMIRAM. E o pedido do dono, e o DM nao tem conserto pra "
				   + "copiar -- la o olho e overlay de toda raca, sem crivo nenhum, e as pupilas ficam "
				   + "flutuando acima do casco da barata");

		for (int i = 2; i <= 4 && i < _retratos.Count; i++)
			Conferir(_retratos[i].OlhosVisiveis,
					 $"pose {i} ({rotulo[i]}): os OLHOS VOLTARAM -- os degraus acima da larva sao "
				   + "humanoides desenhados, e apaga-los neles seria trocar um defeito por outro");

		if (_retratos.Count > 5)
			Conferir(_retratos[5].RaiosEmitindo,
					 "pose 5 (SUPER PERFEITA): os RAIOS estao emitindo -- e sao eles, e nao a aura, que "
				   + "fazem esta forma se ver na tela (`Raios = 2` na entrada do catalogo)");

		// ============================ E AQUI A BANCADA COBRA O **CONTRARIO**, PORQUE A REGRA E DO DONO ============================
		// A aura deste port NAO acende por estar transformado: *"so aparece quando o ki passa de 100%;
		// um Super Saiyajin parado deixa de brilhar"* (`World.PrepararAuraDaForma`). Entao exigir aura na
		// pose 6 seria a bancada exigindo um defeito -- e foi exatamente o que a primeira versao desta
		// linha fez, reprovando o jogo por estar certo.
		//
		// A prova de que o DNA chega ate a tela mudou de lugar: ela e a pose 7, com o tanque acima de
		// 100%. Aqui a cobranca e que a pose 6 esteja APAGADA, que e a regra.
		// ====================================================================================================================
		if (_retratos.Count > 6)
			Conferir(!_retratos[6].AuraAcesa,
					 "pose 6 (SUPER SAIYAJIN parado): a aura esta APAGADA -- e isso e a REGRA e nao um "
				   + "buraco: neste port a aura significa 'estou passando do meu limite', e um Super "
				   + "Saiyajin parado nao esta");

		if (_retratos.Count > 7)
		{
			Conferir(_retratos[7].Contorno > 0.01f,
					 "pose 7 (SUPER SAIYAJIN carregando): o CONTORNO da forma ACENDEU no corpo "
				   + $"({_retratos[7].Contorno:0.000}) -- **esta** e a prova de que o DNA Saiyajin do "
				   + "doador chega ate a tela de quem esta olhando");

			// O CONTROLE DA LINHA DE CIMA, e ele e a metade que separa "acendeu" de "estava aceso o
			// tempo todo": nas sete poses anteriores o corpo esteve com o Ki dentro do limite, e o
			// contorno tem que ter ficado em zero em todas elas.
			Conferir(_retratos.Take(7).All(r => r.Contorno <= 0.01f),
					 "...e ele estava em ZERO nas sete poses anteriores -- o contorno significa 'passei "
				   + "do meu limite', e nao 'estou transformado'");
		}
	}

	/// <summary>
	/// ============================ O PIXEL, QUE E A UNICA PERGUNTA QUE SO A FOTO RESPONDE ============================
	/// A folha certa no material nao e a imagem certa na tela -- e literalmente a licao que este projeto
	/// tem escrita ("uniform escrito nao e pixel desenhado"). Um sprite trocado por um `.tres` que
	/// aponta pro atlas errado, uma pose sem quadros, uma camada invisivel: os tres deixam a folha certa
	/// e a tela igual.
	///
	/// A MATRIZ INTEIRA e nao so os pares vizinhos: dois degraus NAO consecutivos que saiam iguais e o
	/// mesmo defeito (a lista de corpos com uma entrada repetida), e ele passaria por uma checagem de
	/// vizinhos.
	/// ==========================================================================================================
	/// </summary>
	private void ADiferencaDePixel()
	{
		var comFoto = _retratos.Where(r => r.Corte != null).ToList();
		if (comFoto.Count < 2) { Conferir(false, "ha retratos suficientes pra comparar"); return; }

		GD.Print("[bio-foto] --- diferenca entre os retratos, em % de pixels (ultima coluna: o RUIDO "
			   + "do cenario medido na propria pose) ---");
		for (int i = 0; i < comFoto.Count; i++)
		{
			string linha = $"[bio-foto]   pose {comFoto[i].Pose}: ";
			for (int j = 0; j < comFoto.Count; j++)
				linha += j == i ? "   -- " : $"{Diferenca(comFoto[i], comFoto[j]) * 100,5:0.0}%";
			linha += comFoto[i].Ruido < 0 ? "   | ruido nao medido"
										  : $"   | ruido {comFoto[i].Ruido * 100:0.0}%";
			GD.Print(linha);
		}

		Conferir(comFoto.Count(r => r.Ruido >= 0) >= comFoto.Count - 1,
				 "o RUIDO do cenario foi medido em cada pose (o segundo clique da mesma pose) -- sem "
			   + "ele, a linha de baixo estaria comparando mato com mato e chamando de escada");

		for (int i = 1; i < comFoto.Count; i++)
			ODegrauDesenhaDiferente(comFoto[i - 1], comFoto[i], "consecutiva");

		// ============================ A POSE 6 CONTRA A 4 -- A UNICA QUE NAO TROCA DE CORPO ============================
		// O Super Saiyajin do bio nao veste sprite nenhum: `HairObject.dm:168` proibe cabelo pro bio em
		// TODA forma, e a folha do corpo continua sendo a perfeita. Entao esta e a unica pose cuja prova
		// visual e SO aura e raios -- e ela nao entra no laco dos degraus (que compara corpos) nem no
		// dos vizinhos (a 5 esta no meio). Sem esta linha, um DNA Saiyajin que chegasse ate a ficha e
		// nao ate a tela sairia com placar limpo.
		// ==========================================================================================================
		// ============================ O PAR QUE TEM QUE SAIR **IGUAL**, E ELE E O ACHADO DESTA FOTO ============================
		// A pose 6 (Super Saiyajin parado) e a pose 4 (forma perfeita) sao a MESMA imagem, e as tres
		// razoes sao regras escritas: o bio nao ganha cabelo em forma nenhuma (`HairObject.dm:168`), o
		// SSJ1 nao tem raios, e a aura so acende acima de 100% de Ki (regra do dono). Ou seja: **um
		// bio-androide em Super Saiyajin, parado, nao se distingue de um bio-androide comum.**
		//
		// Isso e uma resposta e nao uma falha, e ela vale ser dita: quem quiser ver o Super Saiyajin de
		// um bio tem que ve-lo carregando (a pose 7). A bancada cobra a igualdade porque o contrario --
		// um par que devia ser igual saindo diferente -- significaria efeito vazando de outra forma.
		// ================================================================================================================
		if (comFoto.Count > 6)
		{
			double d46 = Diferenca(comFoto[4], comFoto[6]);
			Conferir(d46 < PisoDaDiferenca,
					 $"a pose 6 (SUPER SAIYAJIN parado) sai IGUAL a 4 (FORMA PERFEITA) -- {d46 * 100:0.0}% "
				   + "de pixels diferentes. Nao e defeito: o bio nao ganha cabelo, o SSJ1 nao tem raios "
				   + "e a aura so acende acima de 100% de Ki. Quem quiser VER o Super Saiyajin de um bio "
				   + "tem que ve-lo carregando -- e essa e a pose 7");
		}

		if (comFoto.Count > 7)
			ODegrauDesenhaDiferente(comFoto[4], comFoto[7],
									"o SUPER SAIYAJIN **CARREGANDO** contra a FORMA PERFEITA -- e aqui a "
								  + "prova visual e SO a aura, porque o corpo e o mesmo. E o unico quadro "
								  + "desta escada em que o DNA Saiyajin aparece na tela");

		// E OS QUATRO DEGRAUS ENTRE SI: a lista `BioAndroids.Corpos` com uma entrada repetida daria
		// vizinhos diferentes e um par distante identico.
		for (int i = 1; i <= 4 && i < comFoto.Count; i++)
			for (int j = i + 1; j <= 4 && j < comFoto.Count; j++)
				ODegrauDesenhaDiferente(comFoto[i], comFoto[j], "degraus nao vizinhos");
	}

	/// <summary>
	/// DOIS RETRATOS SAO DIFERENTES NA TELA? A pergunta tem que passar por cima do RUIDO que o proprio
	/// cenario faz -- ver o cabecalho de <see cref="Retrato"/>. O piso e o maior entre a constante e
	/// tres vezes o ruido medido nas duas poses.
	/// </summary>
	private void ODegrauDesenhaDiferente(Retrato a, Retrato b, string porque)
	{
		double d = Diferenca(a, b);
		double ruido = Math.Max(Math.Max(a.Ruido, b.Ruido), 0);

		Conferir(d >= PisoDaDiferenca,
				 $"a pose {b.Pose} DESENHA diferente da {a.Pose} ({d * 100:0.0}% dos pixels que ficaram "
			   + $"PARADOS nas duas poses, contra um piso de {PisoDaDiferenca * 100:0.0}%; o que se "
			   + $"mexia sozinho -- {ruido * 100:0.0}% do quadro -- saiu da conta) [{porque}]");
	}

	private static bool Difere(Image a, Image b, int x, int y)
	{
		Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
		return Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B) > ToleranciaDoPixel;
	}

	private static double Diferenca(Image a, Image b) => Diferenca(a, b, null, null);

	/// <summary>
	/// ============================ O QUE SE MEXE SOZINHO SAI DA CONTA ============================
	/// A grama nao e o problema -- o problema e que a Terra tem cento e quarenta e oito CIDADAOS, e um
	/// deles atravessando o recorte entre uma pose e outra pinta trinta por cento dos pixels. A bancada
	/// mediu isso na propria pele: o "ruido" da mesma pose deu 0,0% em quatro poses e 14,5% numa delas,
	/// que e a assinatura de um transeunte e nao de luz.
	///
	/// Cada pose tem DOIS cliques (ver <see cref="Retrato"/>), e a diferenca entre eles marca os pixels
	/// que se mexem sozinhos. Esta funcao aceita as duas mascaras e ignora todo pixel que qualquer uma
	/// das duas poses considerou instavel: o que sobra e o CENARIO PARADO mais o boneco -- e o boneco e
	/// a unica coisa que a escada deveria estar mudando.
	/// ==========================================================================================
	/// </summary>
	private static double Diferenca(Image a, Image b, Image? bisA, Image? bisB)
	{
		int w = Math.Min(a.GetWidth(), b.GetWidth()), h = Math.Min(a.GetHeight(), b.GetHeight());
		if (w == 0 || h == 0) return 0;

		int n = 0, contados = 0;
		for (int y = 0; y < h; y += 2)
			for (int x = 0; x < w; x += 2)
			{
				if (bisA != null && x < bisA.GetWidth() && y < bisA.GetHeight() && Difere(a, bisA, x, y))
					continue;
				if (bisB != null && x < bisB.GetWidth() && y < bisB.GetHeight() && Difere(b, bisB, x, y))
					continue;
				contados++;
				if (Difere(a, b, x, y)) n++;
			}
		return contados == 0 ? 0 : (double)n / contados;
	}

	/// <summary>A diferenca entre duas poses, ja sem o que se mexe sozinho. Ver acima.</summary>
	private static double Diferenca(Retrato a, Retrato b) =>
		Diferenca(a.Corte!, b.Corte!, a.Bis, b.Bis);

	/// <summary>
	/// A TIRA: os sete lado a lado, numerados, que e o arquivo que o dono vai abrir.
	///
	/// ============================ ELA SAIU PRETA NA PRIMEIRA RODADA, COM AS SETE FOTOS BOAS NO DISCO ============================
	/// `BlitRect` copia byte a byte e **nao converte formato**: o recorte sai do viewport em `Rgb8` e a
	/// folha nasceu `Rgba8`, entao a copia caia num vazio e a tira ficava com a cor de fundo inteira --
	/// com os `bio-retrato-N.png` individuais perfeitos ao lado. E o mesmo modo de falha que esta
	/// bancada existe pra pegar, so que dentro dela: o numero (a matriz de diferenca) estava certo, o
	/// arquivo que o dono ia abrir estava vazio, e o placar fechava limpo. Achado OLHANDO a imagem.
	/// ====================================================================================================================
	/// </summary>
	private void ATira()
	{
		var comFoto = _retratos.Where(r => r.Corte != null).ToList();
		if (comFoto.Count == 0) { Anotar("sem tira: nenhuma foto saiu"); return; }

		const int Vao = 10, AlturaDoRotulo = 28;
		int larg = comFoto.Sum(r => r.Corte!.GetWidth()) + Vao * (comFoto.Count + 1);
		int alt = comFoto.Max(r => r.Corte!.GetHeight()) + Vao * 2 + AlturaDoRotulo;

		Image tira = Image.CreateEmpty(larg, alt, false, Image.Format.Rgba8);
		tira.Fill(new Color(0.06f, 0.06f, 0.08f));

		int x = Vao;
		foreach (Retrato r in comFoto)
		{
			Image c = (Image)r.Corte!.Duplicate();
			c.Convert(Image.Format.Rgba8);      // ver o cabecalho: sem isto a tira sai PRETA
			tira.BlitRect(c, new Rect2I(0, 0, c.GetWidth(), c.GetHeight()), new Vector2I(x, Vao));
			Numerar(tira, r.Pose, x + c.GetWidth() / 2, alt - AlturaDoRotulo + 4);
			x += c.GetWidth() + Vao;
		}

		string caminho = ProjectSettings.GlobalizePath($"{Pasta}bio-escada.png");
		tira.SavePng(caminho);

		// ============================ E A TIRA E MEDIDA, COMO QUALQUER OUTRA COISA ============================
		// Ela ja saiu PRETA uma vez com as sete fotos boas no disco e o placar limpo (ver o cabecalho).
		// Um arquivo chamado "a escada do bio" sem escada nenhuma vira, meses depois, uma prova que
		// ninguem deu -- e este projeto ja tem seis casos assim.
		// ===================================================================================================
		var fundo = new Color(0.06f, 0.06f, 0.08f);
		int pintados = 0, total = 0;
		for (int y = Vao; y < alt - AlturaDoRotulo; y += 3)
			for (int px = 0; px < larg; px += 3)
			{
				total++;
				Color c2 = tira.GetPixel(px, y);
				if (Math.Abs(c2.R - fundo.R) + Math.Abs(c2.G - fundo.G) + Math.Abs(c2.B - fundo.B) > 0.05f)
					pintados++;
			}

		Conferir(total > 0 && (double)pintados / total > 0.5,
				 $"A TIRA saiu com {comFoto.Count} retrato(s) lado a lado, NUMERADOS, e ela NAO esta "
			   + $"vazia ({(total > 0 ? 100.0 * pintados / total : 0):0}% dos pixels sao imagem e nao "
			   + $"fundo) -> {caminho}");
	}

	/// <summary>
	/// O NUMERO DA POSE, desenhado a mao em 3x5. Sem rotulo, a tira e sete bonecos verdes e quem olha
	/// tem que contar da esquerda pra direita adivinhando onde a escada comeca -- que e exatamente o
	/// tipo de prova que, meses depois, nao prova nada.
	/// </summary>
	private static readonly string[] Digitos =
	[
		"###" + "#.#" + "#.#" + "#.#" + "###",   // 0
		".#." + "##." + ".#." + ".#." + "###",   // 1
		"###" + "..#" + "###" + "#.." + "###",   // 2
		"###" + "..#" + "###" + "..#" + "###",   // 3
		"#.#" + "#.#" + "###" + "..#" + "..#",   // 4
		"###" + "#.." + "###" + "..#" + "###",   // 5
		"###" + "#.." + "###" + "#.#" + "###",   // 6
		"###" + "..#" + "..#" + "..#" + "..#",   // 7
		"###" + "#.#" + "###" + "#.#" + "###",   // 8
		"###" + "#.#" + "###" + "..#" + "###",   // 9
	];

	private static void Numerar(Image tira, int numero, int centroX, int topoY)
	{
		const int Escala = 4;
		string d = Digitos[Math.Clamp(numero, 0, 9)];
		int x0 = centroX - 3 * Escala / 2;
		var branco = new Color(0.85f, 0.9f, 0.85f);

		for (int linha = 0; linha < 5; linha++)
			for (int coluna = 0; coluna < 3; coluna++)
			{
				if (d[linha * 3 + coluna] != '#') continue;
				for (int y = 0; y < Escala; y++)
					for (int x = 0; x < Escala; x++)
					{
						int px = x0 + coluna * Escala + x, py = topoY + linha * Escala + y;
						if (px >= 0 && py >= 0 && px < tira.GetWidth() && py < tira.GetHeight())
							tira.SetPixel(px, py, branco);
					}
			}
	}
}
