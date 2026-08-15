using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ OS TRES PEDIDOS VISUAIS DO DONO, FOTOGRAFADOS (`--diagolhar`) ============================
/// Sobe junto do `--bioolhar` do servidor (`GameServer.BioOlhar.cs`), que planta tres corpos em volta
/// de quem esta olhando e roda o roteiro pelas portas de producao.
///
/// ============================ POR QUE ELA NAO E O `--diagbio` ============================
/// Aquela bancada tira UMA foto por degrau e compara os recortes entre si, com um piso de 3% de
/// pixels. Ela e boa no que faz e **nao consegue ver nenhuma das tres coisas aqui**:
///
///   * os olhos da larva sao ~4 px num recorte de 76x96. Eles nao chegam nem perto do piso de 3%, e a
///     pose da larva ja passa com folga porque o CORPO INTEIRO trocou de sprite. Ou seja: o defeito
///     que o dono fotografou e invisivel pra aquela maquinaria, e por isso ela o mede no NODE
///     (`OlhosVisiveisDeTeste`) -- que e "uniform escrito", nao "pixel desenhado";
///   * a cinematica dura 28 s e aquele palco existe pra **nao** ter cena rodando;
///   * e a morte precisa de dois corpos no mesmo quadro.
///
/// ============================ COMO ELA MEDE: O MESMO QUADRO, COM E SEM A CAMADA ============================
/// Nenhuma das tres perguntas se responde comparando duas fotos tiradas em INSTANTES diferentes -- o
/// mato se mexe, a luz do dia anda, e a coisa medida (quatro pixels de olho) e menor que o ruido.
///
/// Entao a medida e outra: **injeta-se o defeito e fotografa-se de novo, tres quadros depois**. A
/// diferenca entre as duas fotos e, por construcao, exatamente a camada que se ligou ou desligou. E
/// cada medida carrega o proprio controle de ruido: um par tirado com o MESMO intervalo e sem mexer em
/// nada, pra a bancada saber quanto o cenario se mexe sozinho naquela janela.
///
///   olhos  -- `CharacterVisual.OlhosForcadosDeTeste`, que so a bancada escreve;
///   brilho -- `CharacterVisual.SilhuetaDeCena(null)` e a devolucao dela.
///
/// E ISSO DA OS DOIS SENTIDOS DE GRACA, que e o que separa "consertei" de "apaguei":
///   * na LARVA, forcar os olhos tem que MUDAR pixels (o desenho chegaria ali -- o conserto trabalha)
///     e a producao tem que ser IGUAL a versao apagada (zero pixel de olho na tela);
///   * num corpo HUMANOIDE e o contrario exato, com as mesmas duas contas trocadas de lado.
/// ==============================================================================================================
/// </summary>
public partial class RoboDeOlharDoBio : Node
{
	private const string Pasta = "user://";

	/// <summary>
	/// O RECORTE. Menor que o do `--diagbio` (76x96) de proposito: aqui a coisa medida tem quatro
	/// pixels, e cada linha de mato a mais no quadro e ruido puro competindo com ela. Alto o bastante
	/// pra caber o espaco ACIMA da cabeca, que e onde as pupilas do dono estavam flutuando.
	/// </summary>
	private const int Largura = 64, Altura = 80;

	/// <summary>Quanto ampliar a foto que vai pro disco. A MEDIDA e feita no recorte cru.</summary>
	private const int Ampliacao = 5;

	/// <summary>
	/// Quanto dois pixels precisam diferir pra contarem como diferentes (soma dos tres canais).
	///
	/// O MESMO 0,30 do `--diagbio`, e pela mesma calibragem: abaixo disso a grama animada e a luz do
	/// dia sozinhas pintam um terco do recorte. Nao ha razao pra este numero ser outro -- e havendo
	/// duas constantes, um dia elas discordariam sobre a mesma pergunta.
	/// </summary>
	private const float ToleranciaDoPixel = 0.30f;

	/// <summary>
	/// QUANTOS QUADROS ESPERAR entre mexer numa camada e fotografar.
	///
	/// TRES E NAO UM: o `GetImage` do viewport devolve o quadro que a GPU JA desenhou, ou seja ele
	/// atrasa. Com um quadro so, metade das fotos sairia com o estado ANTERIOR -- e o sintoma disso e
	/// o pior possivel numa bancada visual: ela nao quebra, ela mede a coisa errada e fica verde.
	/// </summary>
	private const int QuadrosDeEspera = 3;

	private int _a, _b, _c;
	private int _passo = -1;
	private bool _fechou, _ocupado;
	private double _relogio;

	/// <summary>Segundos totais antes de fechar sozinha, mesmo com passos faltando.</summary>
	public double Fim = 240;

	private int _oks;
	private readonly List<string> _falhas = [];
	private readonly List<string> _linhas = [];

	/// <summary>Qual forma (id de rede) chegou pra cada corpo, pelo `S2C.Forma`.</summary>
	private readonly Dictionary<int, int> _formaDe = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		GD.Print("[olhar] " + (ok ? "  ok   " : "  FALHA") + "  " + oque);
	}

	private void Anotar(string s) { _linhas.Add(s); GD.Print("[olhar] " + s); }

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Falou += AoOuvir;
		cli.FormaMudou += (quem, _, para, _, _) => _formaDe[quem] = para;
		GD.Print("[olhar] no ar -- esperando o elenco do `--bioolhar`");
	}

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (!_fechou && _relogio >= Fim) Fechar();
	}

	private void AoOuvir(Jandirus.Net.Protocol.Fala tipo, string quem, string texto)
	{
		string elenco = Jandirus.Server.GameServer.MarcaDoElencoDoOlhar;
		int i = texto.IndexOf(elenco, StringComparison.Ordinal);
		if (i >= 0)
		{
			string[] ids = texto[(i + elenco.Length)..].Trim().Split(',');
			if (ids.Length == 3 && int.TryParse(ids[0], out _a) && int.TryParse(ids[1], out _b)
				&& int.TryParse(ids[2], out _c))
				Anotar($"elenco: A={_a} (bio COM DNA) | B={_b} (Saiyajin) | C={_c} (bio SEM DNA)");
			return;
		}

		string marca = Jandirus.Server.GameServer.MarcaDoOlharDoBio;
		i = texto.IndexOf(marca, StringComparison.Ordinal);
		if (i < 0 || !int.TryParse(texto[(i + marca.Length)..].Trim(), out int passo)) return;
		if (passo == _passo) return;

		_passo = passo;
		Anotar($"o palco anunciou o passo {passo}");
		_ = Roteiro(passo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Task Roteiro(int passo)
	{
		if (_ocupado) { Anotar($"     passo {passo} chegou com a bancada ocupada -- pulado"); return; }
		_ocupado = true;
		try
		{
			switch (passo)
			{
				// ============================ O PEDIDO 1 -- OS OLHOS ============================
				// Tres poses e nao uma, porque as tres quebram separadas: a 0 e o CONTROLE (sem ela,
				// "a larva nao tem olhos" ficaria verde num port que perdeu a camada inteira), a 1 e
				// o pedido, e a 2 e a metade que impede o conserto de virar exagero -- o degrau acima
				// da larva e um humanoide desenhado, e apagar o olho nele seria trocar um defeito por
				// outro.
				// =============================================================================
				case 0: await Esperar(2.0); await OsOlhos(_a, "HUMANO", temRosto: true); break;
				case 1: await Esperar(2.0); await OsOlhos(_a, "LARVA", temRosto: false); break;
				case 2: await Esperar(2.0); await OsOlhos(_a, "IMPERFEITO", temRosto: true); break;

				// ============================ O PEDIDO 4 -- A CINEMATICA ============================
				// TREZE SEGUNDOS: a metamorfose mede 28,0 s e o dono pediu o MEIO dela. Antes dos 3 s
				// ainda ha o beat de abertura assentando; depois dos 16 s entram os feixes de chao, que
				// pintam meio recorte e afogariam a medida da silhueta no proprio efeito.
				// ================================================================================
				case 3:
					await Esperar(13.0);
					OVereditoDoBrilho(await OBrilho(_a, "BIO-ANDROIDE (metamorfose)"),
									  await OBrilho(_b, "SAIYAJIN (Super Saiyajin)"));
					break;

				case 4: await Esperar(32.0); await Retrato(_a, "forma-perfeita"); break;

				// ============================ O PEDIDO 3 -- A MORTE ============================
				case 5: await Esperar(2.0); await AMorte("antes"); break;
				case 6: await Esperar(1.5); await AMorte("depois"); break;
				case 7: await Esperar(2.0); await AMorte("dez-segundos-depois"); Fechar(); break;
			}
		}
		catch (Exception e) { Anotar($"     a bancada tropecou no passo {passo}: {e.Message}"); }
		finally { _ocupado = false; }
	}

	private async Task Esperar(double s)
	{
		double ate = _relogio + s;
		while (_relogio < ate && !_fechou)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Quadros(int n)
	{
		for (int k = 0; k < n && !_fechou; k++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// =====================================================================
	// 1. OS OLHOS -- o mesmo quadro, com e sem a camada
	// =====================================================================
	/// <summary>
	/// ============================ A SEQUENCIA, E CADA FOTO TEM UM PAR VIZINHO ============================
	/// Cinco fotos em ordem, todas com o MESMO intervalo de tres quadros entre elas:
	///
	///     p0  producao      \__ o RUIDO: o que o cenario se mexe sozinho nesta janela
	///     p1  producao      /
	///     on  olhos FORCADOS ACESOS  -> comparada com a p1, que e a vizinha dela
	///     p2  producao               -> volta ao normal, e vira a vizinha da proxima
	///     off olhos FORCADOS APAGADOS-> comparada com a p2
	///
	/// Comparar sempre com a VIZINHA e o que mantem a medida honesta: p0 e off estao dez quadros
	/// separadas, e nessa distancia o mato ja pinta mais que o olho.
	/// ==================================================================================================
	/// </summary>
	private async Task OsOlhos(int id, string rotulo, bool temRosto)
	{
		if (Visual(id) is not { } vis) { Conferir(false, $"[olhos/{rotulo}] achei o corpo pra fotografar"); return; }

		int ruido = int.MaxValue, comOlho = 0, semOlho = 0;
		Foto? guardaProd = null, guardaOn = null, guardaOff = null;
		Rect2I? mascara = null;

		// ============================ TRES RODADAS, E FICA A DE MENOS RUIDO ============================
		// A primeira rodada boa desta bancada reprovou a pose IMPERFEITA com "ruido 32 px, o olho muda
		// 16" -- ou seja, o transeunte que atravessou o recorte naqueles tres quadros pintou o DOBRO do
		// que a coisa medida pinta. E o mesmo defeito que o `--diagbio` documenta em si mesmo, e ele nao
		// se resolve com constante nenhuma: a Terra tem cento e quarenta e oito cidadaos andando.
		//
		// Repetir e escolher a janela mais limpa NAO e escolher o resultado que agrada -- o criterio e
		// o RUIDO, que e medido sem tocar em camada nenhuma, e a medida do olho vem de carona com ele.
		// Uma janela em que o cenario esta parado e simplesmente uma janela em que da pra medir 4 px.
		// ==========================================================================================
		for (int rodada = 0; rodada < 3; rodada++)
		{
			// A MASCARA E LIDA A CADA RODADA, e nao uma vez: o corpo pode andar, e a caixa e em
			// coordenadas de tela. Ela e a caixa da CAMADA DE OLHOS -- que responde mesmo com a camada
			// apagada, que e justamente o caso da larva.
			Rect2I? m = EmTela(vis, vis.CaixaDosOlhosDeTeste, folga: 2);

			Foto? p0 = Recortar(id);
			await Quadros(QuadrosDeEspera);
			Foto? p1 = Recortar(id);

			vis.OlhosForcadosDeTeste = true;
			await Quadros(QuadrosDeEspera);
			Foto? on = Recortar(id);

			vis.OlhosForcadosDeTeste = null;
			await Quadros(QuadrosDeEspera);
			Foto? p2 = Recortar(id);

			vis.OlhosForcadosDeTeste = false;
			await Quadros(QuadrosDeEspera);
			Foto? off = Recortar(id);

			vis.OlhosForcadosDeTeste = null;

			if (p0 == null || p1 == null || on == null || p2 == null || off == null || m == null) continue;

			int r = Diferenca(p0, p1, m);
			Anotar($"     olhos/{rotulo} rodada {rodada + 1}: caixa dos olhos {m} | ruido {r} px | "
				 + $"ACESO {Diferenca(p1, on, m)} | APAGADO {Diferenca(p2, off, m)}");
			if (r >= ruido) continue;

			ruido = r;
			comOlho = Diferenca(p1, on, m);
			semOlho = Diferenca(p2, off, m);
			guardaProd = p1; guardaOn = on; guardaOff = off; mascara = m;
		}

		if (guardaProd == null || guardaOn == null || guardaOff == null)
		{ Conferir(false, $"[olhos/{rotulo}] as fotos renderam (no headless o GetImage volta vazio)"); return; }

		Salvar(guardaProd, $"olhos-{rotulo.ToLowerInvariant()}-1-producao");
		Salvar(guardaOn, $"olhos-{rotulo.ToLowerInvariant()}-2-forcando-o-olho");
		Salvar(guardaOff, $"olhos-{rotulo.ToLowerInvariant()}-3-forcando-sem-olho");
		Tira($"olhos-{rotulo.ToLowerInvariant()}", [guardaProd, guardaOn, guardaOff]);

		Anotar($"olhos/{rotulo}: node diz OLHOS={vis.OlhosVisiveisDeTeste} | ruido {ruido} px | "
			 + $"forcando ACESO muda {comOlho} px | forcando APAGADO muda {semOlho} px "
			 + $"(medido DENTRO da caixa dos olhos, {mascara}, a mais limpa de 3 rodadas)");

		if (temRosto)
		{
			// ============================ O CORPO E DE GENTE: OS OLHOS TEM QUE ESTAR NA TELA ============================
			// Este e o CONTRA-EXEMPLO do pedido, e sem ele o pedido nao significa nada: um port que
			// tivesse perdido a camada de olhos inteira passaria por "a larva nao tem olhos" com nota
			// cheia, e o defeito seria pior que o original.
			// ======================================================================================================
			Conferir(semOlho > ruido,
					 $"[olhos/{rotulo}] APAGAR os olhos MUDA a tela ({semOlho} px, contra {ruido} de "
				   + "ruido) -- ou seja eles estao desenhados neste corpo. E o contra-exemplo do pedido "
				   + "do dono: um port sem camada de olho nenhuma passaria pela larva e reprovaria aqui");

			Conferir(comOlho <= ruido,
					 $"[olhos/{rotulo}] ...e FORCA-LOS aceso nao muda nada ({comOlho} px, contra "
				   + $"{ruido} de ruido) -- a producao ja e a versao com olho");
		}
		else
		{
			// ============================ A LARVA: **O PEDIDO**, NOS DOIS SENTIDOS ============================
			// *"os bio androides o OLHO FICA VOANDO na forma de BARATA, faca com q os olhos SUMAM ao
			// ficar nessa forma"* -- com foto das duas pupilas flutuando acima do casco.
			//
			// A primeira linha e a que prova que o CONSERTO TRABALHA: se o olho fosse desenhado ali de
			// qualquer jeito, forcar a camada nao mudaria pixel nenhum e a segunda linha ficaria verde
			// sozinha num port que nunca teve olhos. A segunda e a que o dono ve.
			// ===========================================================================================
			Conferir(comOlho > ruido,
					 $"[olhos/LARVA] o olho REALMENTE cairia sobre a barata: forcando a camada, "
				   + $"{comOlho} px da tela mudam (contra {ruido} de ruido). E o defeito que o dono "
				   + "fotografou, reproduzido -- sem esta linha, 'a larva nao tem olhos' ficaria verde "
				   + "num port que nunca desenhou olho nenhum");

			Conferir(semOlho <= ruido,
					 $"[olhos/LARVA] **e em producao a barata sai LIMPA**: apagar a camada de olhos nao "
				   + $"muda nada na tela ({semOlho} px, contra {ruido} de ruido) -- ou seja ZERO pixel "
				   + "de olho esta sendo desenhado sobre a larva. E o pedido do dono, medido no PIXEL");
		}
	}

	// =====================================================================
	// 2. O BRILHO DA CINEMATICA -- o overlay `bioto2`/`bioto3`
	// =====================================================================
	/// <summary>
	/// A MESMA RECEITA, com a camada de silhueta no lugar da de olhos: fotografa o meio da cena,
	/// APAGA o overlay de luz, fotografa de novo e devolve.
	///
	/// ============================ AS DUAS JANELAS TEM QUE TER O MESMO TAMANHO ============================
	/// A primeira versao media o ruido entre a PRIMEIRA e a TERCEIRA foto (seis quadros) e o brilho
	/// entre a terceira e a segunda (tres). Numa cena calma isso nao aparece -- o bio deu 1411 contra 48
	/// e nenhum arredondamento muda esse veredito. Na cena do Super Saiyajin, que e uma chuva de raios e
	/// particulas, os numeros sairam 518 contra 383 e a comparacao virou cara-ou-coroa: metade do ruido
	/// que ela cobrava era so a janela ser o dobro da outra.
	/// ================================================================================================
	///
	/// Devolve (folha, ruido, brilho) em vez de julgar: o veredito e uma comparacao ENTRE as duas
	/// racas, e nenhuma das duas sozinha pode faze-la. Ver <see cref="OVereditoDoBrilho"/>.
	/// </summary>
	private async Task<Medida> OBrilho(int id, string rotulo)
	{
		if (Visual(id) is not { } vis)
		{ Conferir(false, $"[brilho/{rotulo}] achei o corpo"); return new Medida(null, 0, 0, []); }

		string? folha = vis.SilhuetaDeCenaDeTeste;

		int ruido = int.MaxValue, brilho = 0;
		Foto? guardaCom = null, guardaSem = null;
		List<int> todos = [];

		// ============================ TRES RODADAS AQUI TAMBEM, E ELAS APERTAM O CONTRA-EXEMPLO ============================
		// Uma cinematica nao tem um ruido, tem uma FAIXA: numa rodada o mesmo instante do mesmo filme
		// mediu 48 px de cena andando sozinha, na seguinte mediu 480 -- dez vezes, porque o beat de
		// tremor cai entre um quadro e outro. Com uma amostra so, a relacao do bio saiu 29x numa rodada
		// e 3,3x na outra, e a linha de baixo passou raspando.
		//
		// E ficar com a rodada de MENOS ruido nao afrouxa o contra-exemplo, aperta: o ruido e o
		// DENOMINADOR da relacao dos dois, entao escolher o menor SOBE a nota do Saiyajin -- que e o
		// lado que a bancada quer ver reprovado.
		// ============================================================================================================
		for (int rodada = 0; rodada < 3; rodada++)
		{
			// O RUIDO PRIMEIRO, e no MESMO intervalo da medida: dois quadros com a cena rodando e
			// nenhuma camada tocada. Numa cinematica isso nao e "mato" -- e a propria cena andando
			// (particulas, raios, a silhueta que e um sprite ANIMADO com relogio proprio).
			// A MASCARA E A CAIXA DO **CORPO**, e nao a da silhueta -- e essa escolha e o que torna o
			// contra-exemplo possivel. A silhueta e a silhueta DO CORPO (mesma folha 32x32), entao a
			// caixa e a mesma; mas quem nao tem silhueta nenhuma (o Saiyajin) nao teria caixa pra
			// oferecer, e a medida das duas racas sairia em reguas diferentes.
			Rect2I? m = EmTela(vis, vis.CaixaDoCorpoDeTeste, folga: 2);

			Foto? c0 = Recortar(id);
			await Quadros(QuadrosDeEspera);
			Foto? c1 = Recortar(id);

			vis.SilhuetaDeCena(null);
			await Quadros(QuadrosDeEspera);
			Foto? sem = Recortar(id);

			vis.SilhuetaDeCena(folha);
			await Quadros(QuadrosDeEspera);

			if (c0 == null || c1 == null || sem == null || m == null) continue;

			int r = Diferenca(c0, c1, m), d = Diferenca(c1, sem, m);
			todos.Add(d);
			Anotar($"     brilho/{rotulo} rodada {rodada + 1}: caixa do corpo {m} | ruido {r} px | "
				 + $"apagar muda {d} px");
			if (r >= ruido) continue;

			ruido = r;
			brilho = d;
			guardaCom = c1; guardaSem = sem;
		}

		if (guardaCom == null || guardaSem == null)
		{ Conferir(false, $"[brilho/{rotulo}] as fotos renderam"); return new Medida(folha, 0, 0, todos); }

		Salvar(guardaCom, $"cena-{Curto(rotulo)}-1-meio-da-cena");
		Salvar(guardaSem, $"cena-{Curto(rotulo)}-2-sem-o-overlay");
		Tira($"cena-{Curto(rotulo)}", [guardaCom, guardaSem]);

		Anotar($"brilho/{rotulo}: silhueta = {(folha == null ? "(nenhuma)" : Nome(folha))} | "
			 + $"ruido {ruido} px | apagar o overlay muda {brilho} px "
			 + $"| relacao {brilho / (double)Math.Max(ruido, 1):0.0}x (a mais limpa de 3 rodadas) "
			 + $"| as tres rodadas: {string.Join(" / ", todos)} px, espalhamento "
			 + $"{Espalhamento(todos):0.00}");

		return new Medida(folha, ruido, brilho, todos);
	}

	/// <summary>O que a medida do brilho devolve. Ver <see cref="OVereditoDoBrilho"/>.</summary>
	private sealed record Medida(string? Folha, int Ruido, int Brilho, IReadOnlyList<int> Rodadas);

	/// <summary>
	/// ============================ O QUANTO AS TRES RODADAS DISCORDAM ENTRE SI, DE 0 A 1 ============================
	/// **E ele e o unico discriminador estavel que esta bancada achou pro contra-exemplo da cinematica.**
	///
	/// Apagar um sprite DE VERDADE tira sempre os MESMOS pixels: no bio as tres rodadas mediram
	/// 1736 / 1639 / 1484 -- espalhamento 0,15. Apagar o que nao existe (a cena do Saiyajin nao tem
	/// silhueta nenhuma) nao tira nada, e o numero que sobra e a chuva de raios da propria cena: as tres
	/// rodadas dele mediram 1925 / 46 / 522 -- espalhamento 0,98.
	///
	/// A primeira versao desta linha comparava a MAGNITUDE (quanto o apagar muda contra quanto a cena
	/// muda sozinha) e era cara-ou-coroa: em quatro rodadas ela deu 29x, 3,3x, 3,6x e 8,5x pro Saiyajin
	/// que nao tem camada nenhuma -- uma delas MAIOR que a do bio na mesma rodada. Ela ficou verde tres
	/// vezes por sorte, e foi a rodada do defeito injetado que a derrubou.
	/// ==========================================================================================================
	/// </summary>
	private static double Espalhamento(IReadOnlyList<int> v) =>
		v.Count < 2 || v.Max() == 0 ? 0 : (v.Max() - v.Min()) / (double)v.Max();

	/// <summary>
	/// ============================ O PEDIDO 4, E ELE SO SE RESPONDE COM AS DUAS RACAS NA MAO ============================
	/// *"vc n colocou a CINEMATICA DE TRANSFORMACAO dos bio androides... tinha um OVERLAY q fazia o
	/// CORPO BRILHAR"*.
	///
	/// A pergunta tem tres metades e nenhuma serve sozinha:
	///
	///   * QUAL folha -- o `bioto2`/`bioto3` do DM (`plane = 7`), e nao uma luz qualquer;
	///   * se ela chega ao PIXEL -- que e a metade que este projeto ja perdeu tres vezes (a cor do
	///     desvio, a barra de vida, o rastro da agua: bancada verde, nada na tela);
	///   * e se ela e **DO BIO**. Sem o contra-exemplo, "a cena acende uma silhueta" poderia significar
	///     que o tocador acende uma em toda transformacao -- e a resposta ao dono seria falsa.
	///
	/// ============================ E A TERCEIRA SE MEDE POR **REPETICAO**, NAO POR MAGNITUDE ============================
	/// "Mexer na camada do Saiyajin nao muda nada" e o que se QUERIA cobrar, e nao da: a cena dele e uma
	/// chuva de raios, entao qualquer par de quadros ja difere em centenas de pixels. Tentei duas
	/// versoes por magnitude (`brilho <= ruido`, depois a relacao dos dois contra a do bio) e as duas
	/// eram cara-ou-coroa -- em quatro rodadas o Saiyajin, que **nao tem camada nenhuma**, mediu 0,7x /
	/// 4,7x / 0,1x / 8,5x, uma delas maior que a do bio na mesma rodada.
	///
	/// O que separa os dois nao e o tamanho do numero, e a REPRODUTIBILIDADE dele: apagar um sprite de
	/// verdade tira sempre os mesmos pixels; apagar o que nao existe deixa o acaso responder. Ver
	/// <see cref="Espalhamento"/>.
	/// ==============================================================================================================
	/// </summary>
	private void OVereditoDoBrilho(Medida bio, Medida saiyajin)
	{
		Conferir(bio.Folha != null && bio.Folha.Contains("bioto", StringComparison.OrdinalIgnoreCase),
				 "[brilho] no MEIO da cena o BIO veste a folha de luz do DM (`bioto2`/`bioto3`, "
			   + $"`plane = 7`) -- chegou \"{(bio.Folha == null ? "(nenhuma)" : Nome(bio.Folha))}\"");

		Conferir(bio.Brilho > bio.Ruido,
				 $"[brilho] **e ela esta DESENHADA**: apagar o overlay muda {bio.Brilho} px da tela, "
			   + $"contra {bio.Ruido} px que a propria cena muda sozinha no mesmo intervalo. E o "
			   + "'CORPO BRILHAR' do pedido, medido no pixel e nao no campo");

		Conferir(saiyajin.Folha == null,
				 "[brilho] o SAIYAJIN se transformando no MESMO instante **nao** veste folha de luz "
			   + "nenhuma -- a silhueta e do bio-androide e nao do tocador de cenas");

		double espBio = Espalhamento(bio.Rodadas), espSai = Espalhamento(saiyajin.Rodadas);
		Conferir(bio.Rodadas.Count >= 3 && saiyajin.Rodadas.Count >= 3 && espBio < espSai / 2,
				 $"[brilho] ...e a diferenca esta na TELA e nao so no campo: apagar a camada do bio tira "
			   + $"SEMPRE os mesmos pixels ({string.Join("/", bio.Rodadas)} px em tres rodadas, "
			   + $"espalhamento {espBio:0.00}), enquanto a mesma operacao no Saiyajin da um numero "
			   + $"diferente a cada vez ({string.Join("/", saiyajin.Rodadas)} px, {espSai:0.00}) -- "
			   + "porque la nao ha camada pra apagar, so a chuva de raios da propria cinematica dele");
	}

	// =====================================================================
	// 3. A MORTE QUE VIRA SUPER SAIYAJIN 2
	// =====================================================================
	private readonly Dictionary<string, (string PoseA, string PoseC)> _poses = [];

	private async Task AMorte(string momento)
	{
		Foto? a = Recortar(_a);
		Foto? c = Recortar(_c);
		string poseA = Visual(_a)?.PoseDeTeste ?? "";
		string poseC = Visual(_c)?.PoseDeTeste ?? "";
		_poses[momento] = (poseA, poseC);

		if (a != null) Salvar(a, $"morte-{momento}-A-com-requisitos");
		if (c != null) Salvar(c, $"morte-{momento}-C-sem-requisitos");
		if (a != null && c != null) Tira($"morte-{momento}", [a, c]);

		// ============================ O QUADRO DO PROPRIO CORPO, ALEM DO RECORTE DA TELA ============================
		// Os dois nao dizem a mesma coisa, e foi preciso os dois pra entender a foto: o recorte diz o
		// que o dono VE, e este diz o que o boneco esta DESENHANDO. O `ko` do bio-androide e um desenho
		// DEITADO na folha (medido: `Bio Android 3.png`, regiao 192,192), entao um corpo com
		// `Animation == "ko"` que apareca de pe no recorte e uma contradicao -- e ela so se resolve
		// olhando as duas medidas lado a lado.
		// ========================================================================================================
		QuadroDoCorpo(_a, $"morte-{momento}-A-QUADRO-DO-CORPO");
		QuadroDoCorpo(_c, $"morte-{momento}-C-QUADRO-DO-CORPO");

		Anotar($"morte/{momento}: A folha \"{Nome(Visual(_a)?.FolhaDoCorpoDeTeste ?? "")}\" "
			 + $"deitado={Visual(_a)?.DeitadoDeTeste} | C folha "
			 + $"\"{Nome(Visual(_c)?.FolhaDoCorpoDeTeste ?? "")}\" deitado={Visual(_c)?.DeitadoDeTeste}");

		Anotar($"morte/{momento}: A (COM os requisitos) pose '{poseA}' forma-de-rede "
			 + $"{(_formaDe.TryGetValue(_a, out int fa) ? fa.ToString() : "-")} | "
			 + $"C (SEM) pose '{poseC}' forma-de-rede "
			 + $"{(_formaDe.TryGetValue(_c, out int fc) ? fc.ToString() : "-")}");

		await Quadros(1);
	}

	private static bool Caido(string pose) => pose.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

	/// <summary>O quadro que o CORPO deste id esta desenhando agora, direto do sprite -- sem cenario.</summary>
	private void QuadroDoCorpo(int id, string nome)
	{
		if (Visual(id)?.QuadroDoCorpoDeTeste() is not { } q) return;
		Image copia = (Image)q.Duplicate();
		copia.Convert(Image.Format.Rgba8);
		copia.Resize(copia.GetWidth() * 8, copia.GetHeight() * 8, Image.Interpolation.Nearest);
		copia.SavePng(ProjectSettings.GlobalizePath($"{Pasta}{nome}.png"));
	}

	private void OVereditoDaMorte()
	{
		if (!_poses.TryGetValue("antes", out var antes)
			|| !_poses.TryGetValue("depois", out var depois)
			|| !_poses.TryGetValue("dez-segundos-depois", out var fim))
		{ Conferir(false, "[morte] os tres momentos foram fotografados (antes / depois / 10 s depois)"); return; }

		// ============================ E ANTES DE TUDO: HOUVE POSE? ============================
		// Sem esta linha as quatro abaixo passam VAZIAS. Aconteceu: numa rodada em que o robo nunca
		// recebeu o elenco, `PoseDeTeste` devolveu `""` nos seis casos, `Caido("")` deu falso, e a
		// bancada carimbou "o bio COM os requisitos continua DE PE" sobre um corpo que ela nunca tinha
		// achado. Uma afirmacao sobre o nada le exatamente igual a uma afirmacao verdadeira.
		// =================================================================================
		Conferir(new[] { antes.PoseA, antes.PoseC, depois.PoseA, depois.PoseC, fim.PoseA, fim.PoseC }
					 .All(p => p.Length > 0),
				 "[morte] os seis retratos leram uma pose de verdade -- sem isto, um corpo que a "
			   + "bancada nunca achou passa por 'esta de pe'");

		// O CONTROLE: antes do golpe os DOIS estao de pe. Sem ele, "o C esta caido depois" nao diz que
		// ele CAIU -- diria so que ele estava caido, e um corpo que nasceu deitado passaria igual.
		Conferir(antes.PoseA.Length > 0 && antes.PoseC.Length > 0
				 && !Caido(antes.PoseA) && !Caido(antes.PoseC),
				 $"[morte] ANTES do golpe os dois estao de pe (A '{antes.PoseA}', C '{antes.PoseC}') -- "
			   + "o controle das duas linhas abaixo");

		// ============================ O PEDIDO 3, E ELE E UM PAR ============================
		// *"ao ter os requisitos quando estiver na FORMA PERFEITA e MORRER, ele vai CANCELAR A MORTE e
		// voltar a vida de forma INSTANTANEA so q agr no SSJ2"*.
		//
		// Os dois corpos sao IDENTICOS menos por um campo (`bio_saiyan_dna`, que veio da fornada), e
		// morreram no mesmo tique pela mesma porta. Entao a diferenca na tela so pode ser o requisito.
		// ================================================================================
		Conferir(depois.PoseA.Length > 0 && !Caido(depois.PoseA),
				 $"[morte] o bio COM os requisitos continua DE PE no quadro seguinte a propria morte "
			   + $"(pose '{depois.PoseA}') -- a morte foi CANCELADA e o corpo se recompos na hora");

		Conferir(Caido(depois.PoseC),
				 $"[morte] **e o SEM os requisitos CAIU** (pose '{depois.PoseC}') -- e a metade que faz "
			   + "do requisito um requisito: um campo separa os dois corpos, e e ele que decide quem "
			   + "levanta");

		Conferir(fim.PoseA.Length > 0 && !Caido(fim.PoseA) && Caido(fim.PoseC),
				 $"[morte] dez segundos depois nada se desfez: A de pe ('{fim.PoseA}'), C caido "
			   + $"('{fim.PoseC}') -- o cancelamento nao foi um piscar de um quadro");

		// E A FORMA QUE CHEGOU PELA REDE. O `AnunciarForma` manda `S2C.Forma` pra zona inteira; um
		// corpo que se recompusesse na BASE (que era o port ate esta rodada -- `Liberar` sem `Entrar`)
		// passaria nas quatro linhas acima e falharia nesta.
		int ssj2 = Jandirus.Core.Forms.Catalogo.Rede("ssj2");
		Conferir(_formaDe.TryGetValue(_a, out int forma) && forma == ssj2,
				 $"[morte] ...e ele voltou **JA NO SSJ2**: o `S2C.Forma` do corpo A trouxe {ssj2} "
			   + $"(chegou {(_formaDe.TryGetValue(_a, out int f2) ? f2.ToString() : "nada")}). Sem esta "
			   + "linha, um port que apenas DESTRAVASSE a forma passaria por 'ele voltou de pe'");

		Conferir(!_formaDe.ContainsKey(_c) || _formaDe[_c] != ssj2,
				 "[morte] ...e o corpo SEM o DNA nao recebeu forma nenhuma -- ele morreu e ficou morto");
	}

	// =====================================================================
	// FOTO E MEDIDA
	// =====================================================================
	private static CharacterVisual? Visual(int id) =>
		id == 0 ? null : World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private async Task Retrato(int id, string nome)
	{
		if (Recortar(id) is { } img) Salvar(img, nome);
		await Quadros(1);
	}

	/// <summary>
	/// A tela inteira, recortada em volta de onde o corpo esta DESENHADO.
	///
	/// ============================ UM RECORTE FORA DA TELA E UMA FOTO QUE NAO EXISTE ============================
	/// O `--diagbio` ja perdeu uma rodada inteira assim (quatro poses "SEM FOTO" com o placar acusando
	/// so o total), e o conserto de la vale aqui: a mensagem diz ONDE o corpo estava e com que zoom,
	/// porque "sem foto" sozinho manda procurar o defeito no headless quando o problema e ENQUADRAMENTO.
	/// =======================================================================================================
	/// </summary>
	private Foto? Recortar(int id)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{ Anotar("     SEM FOTO: o viewport voltou vazio (headless nao renderiza)"); return null; }
		if (World.Instancia?.PosicaoDesenhadaDe(id) is not { } mundo)
		{ Anotar($"     SEM FOTO: o corpo {id} nao tem posicao desenhada"); return null; }

		Vector2 tel = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		var caixa = new Rect2I((int)tel.X - Largura / 2, (int)tel.Y - Altura / 2, Largura, Altura);
		caixa = caixa.Intersection(new Rect2I(0, 0, tela.GetWidth(), tela.GetHeight()));
		if (caixa.Size.X < Largura || caixa.Size.Y < Altura)
		{
			Anotar($"     SEM FOTO: o corpo {id} esta FORA DA TELA -- desenhado em {tel} numa tela de "
				 + $"{tela.GetWidth()}x{tela.GetHeight()}, com zoom {World.Instancia?.ZoomDeTeste}");
			return null;
		}

		Image corte = tela.GetRegion(caixa);
		corte.Convert(Image.Format.Rgba8);
		return new Foto(corte, caixa);
	}

	/// <summary>
	/// Um recorte E DE ONDE ELE VEIO. A segunda metade nao e enfeite: a mascara
	/// (<see cref="EmTela"/>) e calculada em coordenadas de TELA, e sem a origem do corte nao ha como
	/// dizer qual pixel do recorte corresponde a ela.
	/// </summary>
	private sealed record Foto(Image Img, Rect2I Caixa);

	/// <summary>
	/// ============================ ONDE UMA CAMADA CAI NA TELA ============================
	/// A caixa vem do `CharacterVisual` em coordenadas do node `Visual`; aqui ela atravessa a
	/// transformada global do boneco e a da camera. `folga` alarga um pouco: entre o pixel do sprite e
	/// o pixel da tela ha uma escala inteira (o zoom) e um arredondamento, e uma mascara justa demais
	/// cortaria a propria borda do desenho que ela existe pra enquadrar.
	/// </summary>
	private Rect2I? EmTela(CharacterVisual vis, Rect2? local, int folga)
	{
		if (local is not { } r) return null;
		Transform2D t = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * vis.GlobalTransform;
		Vector2 a = t * r.Position, b = t * (r.Position + r.Size);
		return new Rect2I(
			(int)Math.Floor(Math.Min(a.X, b.X)) - folga, (int)Math.Floor(Math.Min(a.Y, b.Y)) - folga,
			(int)Math.Ceiling(Math.Abs(b.X - a.X)) + folga * 2,
			(int)Math.Ceiling(Math.Abs(b.Y - a.Y)) + folga * 2);
	}

	/// <summary>
	/// QUANTOS PIXELS DIFEREM entre dois recortes -- em CONTAGEM e nao em porcentagem.
	///
	/// A porcentagem e a unidade certa pra "os dois degraus desenham bichos diferentes" (o
	/// `--diagbio`), e a errada aqui: quatro pixels de olho num recorte de 64x80 sao 0,08% -- um
	/// numero que se le como zero. A contagem diz "quatro", que e o que a coisa e.
	///
	/// ============================ E A MASCARA E O QUE FAZ A CONTA VALER ALGUMA COISA ============================
	/// Sem ela a bancada compara os 5120 pixels do recorte e mede o BAIRRO: uma rodada saiu com 300 px
	/// de "ruido" -- um vizinho parado dentro do quadro -- contra 16 px de olho, e as tres repeticoes
	/// nao consertaram nada, porque o vizinho nao era um pico, era um vizinho.
	///
	/// Com `mascara`, so contam os pixels onde a camada em questao desenha. Nulo = o recorte inteiro,
	/// que continua sendo o certo pra "o corpo caiu no chao".
	/// ========================================================================================================
	/// </summary>
	private static int Diferenca(Foto a, Foto b, Rect2I? mascara = null)
	{
		int w = Math.Min(a.Img.GetWidth(), b.Img.GetWidth());
		int h = Math.Min(a.Img.GetHeight(), b.Img.GetHeight());

		// A MASCARA VEM EM COORDENADAS DE TELA e as duas fotos podem ter origens diferentes (o corpo
		// anda entre um clique e outro). Comparar pixel a pixel exige que as duas sejam lidas no MESMO
		// ponto da tela, entao o deslocamento de cada uma entra na conta.
		Vector2I da = a.Caixa.Position, db = b.Caixa.Position;
		int x0 = 0, y0 = 0, x1 = w, y1 = h;
		if (mascara is { } m)
		{
			x0 = Math.Max(0, m.Position.X - da.X);
			y0 = Math.Max(0, m.Position.Y - da.Y);
			x1 = Math.Min(w, m.Position.X + m.Size.X - da.X);
			y1 = Math.Min(h, m.Position.Y + m.Size.Y - da.Y);
		}

		int n = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				int bx = x + da.X - db.X, by = y + da.Y - db.Y;
				if (bx < 0 || by < 0 || bx >= b.Img.GetWidth() || by >= b.Img.GetHeight()) continue;
				Color p = a.Img.GetPixel(x, y), q = b.Img.GetPixel(bx, by);
				if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > ToleranciaDoPixel)
					n++;
			}
		return n;
	}

	private void Salvar(Foto foto, string nome)
	{
		Image copia = (Image)foto.Img.Duplicate();
		copia.Resize(copia.GetWidth() * Ampliacao, copia.GetHeight() * Ampliacao,
					 Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"{Pasta}{nome}.png");
		copia.SavePng(caminho);
		Anotar($"     -> {caminho}");
	}

	/// <summary>As fotos lado a lado, com uma faixa preta entre elas. E o que o dono olha.</summary>
	private void Tira(string nome, IReadOnlyList<Foto> fotos)
	{
		if (fotos.Count == 0) return;
		const int folga = 6;
		int w = fotos.Sum(f => f.Img.GetWidth()) + folga * (fotos.Count - 1);
		int h = fotos.Max(f => f.Img.GetHeight());

		Image tira = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		tira.Fill(new Color(0, 0, 0, 1));
		int x = 0;
		foreach (Foto f in fotos)
		{
			tira.BlitRect(f.Img, new Rect2I(0, 0, f.Img.GetWidth(), f.Img.GetHeight()),
						  new Vector2I(x, 0));
			x += f.Img.GetWidth() + folga;
		}
		tira.Resize(tira.GetWidth() * Ampliacao, tira.GetHeight() * Ampliacao,
					Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"{Pasta}TIRA-{nome}.png");
		tira.SavePng(caminho);
		Anotar($"     -> {caminho}   <<< a tira");
	}

	private static string Nome(string caminho) => System.IO.Path.GetFileNameWithoutExtension(caminho);

	private static string Curto(string rotulo) =>
		rotulo.Split(' ')[0].ToLowerInvariant().Replace("-", "");

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Fechar()
	{
		if (_fechou) return;
		_fechou = true;

		GD.Print("\n[olhar] ===== OS TRES PEDIDOS VISUAIS DO BIO-ANDROIDE =====");
		foreach (string l in _linhas) GD.Print("[olhar] " + l);

		// UMA BANCADA VISUAL QUE NAO FOTOGRAFOU NADA REPROVA -- o modo de falha mais perigoso desta
		// familia e fechar com "0 falhas" porque o palco nunca subiu.
		Conferir(_a != 0 && _b != 0 && _c != 0, "a bancada recebeu o elenco (tres corpos)");
		Conferir(_passo >= 7, $"o roteiro chegou ao fim (parou no passo {_passo} de 7)");

		OVereditoDaMorte();

		GD.Print(_falhas.Count == 0
			? $"[olhar] ===== {_oks} ok, 0 falha(s) ====="
			: $"[olhar] ===== {_oks} ok, {_falhas.Count} FALHA(S) =====\n[olhar]   "
			  + string.Join("\n[olhar]   ", _falhas));

		GetTree().Quit();
	}
}
