using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA FICHA DO CORPO ERGUIDO (`--diagreflexo`) ============================
/// O reflexo da mente e a copia do Splitform sao corpos que o servidor ERGUE a partir de um jogador,
/// no meio do jogo, e o dono precisa ve-los vestidos como ele. A ficha de aparencia de um corpo
/// viaja num pacote so (`S2C.PeerLook`), uma vez -- e ate 2026-10-08 esse pacote tinha DOIS
/// escritores no servidor: o `PacoteDeAparencia` (todo corpo) e um `MandarLook` (so esses dois), que
/// nao escrevia o ultimo byte, o tipo de fusao. O cliente le o byte sempre.
///
/// ============================ O DEFEITO NAO E "ESTOURA": E "ESTOURA OU LE LIXO" ============================
/// O `NetDataReader` nao confere o fim do pacote (ver `GameClient.PacotesMalLidos`). No log que
/// denunciou o defeito, a MESMA rodada mergulhou duas vezes: numa o tratador lancou e o reflexo ficou
/// sem ficha; na outra nao lancou, e o byte lido como tipo de fusao era a sobra de outro pacote. Por
/// isso a regua desta bancada NAO e "o tratador nao lancou" (verde nos dias de sorte): e "o cliente
/// nao leu alem do fim do pacote", que reprova nos dois casos.
///
/// ============================ O QUE CADA CENA COBRA, E COM QUE OLHO ============================
///   0. O CONTROLE. Depois do login (dezenas de `PeerLook` de NPC) e de uma troca de roupa, nenhum
///      pacote de aparencia foi mal lido -- a regua nao reprova pacote bem escrito.
///   1. O REFLEXO DA MENTE, erguido pelo `EntrarNaMente` de producao.
///   2. A COPIA DO SPLITFORM, erguida pelo verb `SplitForm` de producao.
///   3. O CONTRA-EXEMPLO: com o segundo escritor de volta, a regua da cena 1 TEM que reprovar.
///
/// Nas cenas 1 e 2 o mesmo corpo e cobrado por CINCO olhos, do fio ate o pixel, porque cada um deles
/// ja mentiu sozinho nesta casa:
///   * o FIO: o cliente leu o pacote inteiro, sem lancar e sem passar do fim;
///   * o MUNDO: a ficha esta no `_looks`, e e a que o corpo tem no servidor (a AUTORIDADE responde);
///   * o DONO: ela e igual a minha -- e eu visto roupa de proposito (ver `VestirNoTeste`);
///   * as CAMADAS: o boneco montou a mesma folha de corpo, as mesmas pecas e o mesmo cabelo que o meu;
///   * o PIXEL: esconder o boneco dele muda tantos pixels da tela quanto esconder o meu. Ficha no
///     mapa nao e corpo na tela; quem responde isto e a diferenca entre dois quadros, com JANELA.
///
/// ============================ OS ATALHOS, DITOS EM VOZ ALTA ============================
/// A bancada entra na mente pelos dois pacotes que a telinha do meditar manda (`C2S.Activity` e
/// `C2S.Habilidade`), sem apertar a tecla M: a porta e da `--diagmergulho`. E o servidor veste o
/// corpo e ensina a divisao por fora (`GameServer.ReflexoTeste.cs`): nem o guarda-roupa nem o
/// aprendizado estao sendo medidos aqui. O que NAO tem atalho e o que se mede -- os dois corpos nascem
/// pelo caminho do jogador e a ficha deles atravessa o fio de verdade.
///
/// COMO RODAR -- um processo, com `--host` (a autoridade responde ao lado) e com JANELA (no headless
/// o olho do pixel diz que nao mediu, em vez de passar de graca):
///
///     python Trailer/bruto/bancada.py reflexo "FIM: \d+ OK" --janela -- --host --rede 7996 --semfoco \
///         --campoteste 8 --horateste 0.5 --diagreflexo --raca Saiyan --conta bancada_reflexo --nome Espelho
/// ==============================================================================================================
/// </summary>
public partial class RoboDoReflexo : Node2D
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private readonly List<string> _linhas = [];
	private int _ok, _falhou, _semMedida;

	private void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; _linhas.Add($"  OK     {oque}"); GD.Print($"[reflexo]   OK    {oque}"); return; }
		_falhou++;
		_linhas.Add($"  FALHA  {oque}   {detalhe}");
		GD.PrintErr($"[reflexo]   FALHA {oque}   {detalhe}");
	}

	/// <summary>A cena NAO MEDIU -- e isto nao e um "ok": o placar conta separado.</summary>
	private void NaoMediu(string oque, string porque)
	{
		_semMedida++;
		_linhas.Add($"  ?????  {oque}   ({porque})");
		GD.PrintErr($"[reflexo]   ????? {oque}   ({porque})");
	}

	private void Nota(string t) { _linhas.Add($"   --    {t}"); GD.Print($"[reflexo]    --   {t}"); }

	// =====================================================================
	// RELOGIO E ESPERA
	// =====================================================================
	private double _relogio;
	private bool _rodando, _fechou;

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (_rodando || _fechou) return;
		if (C is not { Connected: true }) return;
		if (World.Instancia == null) return;
		if (_relogio < 4.0) return;   // o mundo assenta: o primeiro quadro ainda monta pedaco

		_rodando = true;
		_ = Rodar();
	}

	private async Task Quadros(int n)
	{
		for (int i = 0; i < n && !_fechou; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Esperar(double s)
	{
		double ate = _relogio + s;
		while (_relogio < ate && !_fechou)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task<bool> EsperarAte(Func<bool> pronto, double prazo)
	{
		double ate = _relogio + prazo;
		while (_relogio < ate && !_fechou)
		{
			if (pronto()) return true;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		return pronto();
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Task Rodar()
	{
		GD.Print("[reflexo] ============ A FICHA DO CORPO ERGUIDO: O REFLEXO DA MENTE E A COPIA DO SPLITFORM ============");

		if (S == null || C == null)
		{
			Afirmar("o servidor esta neste processo (rode com `--host`)", false,
					"sem `--host` nao ha como perguntar a AUTORIDADE qual corpo ela ergueu");
			Fechar();
			return;
		}

		int eu = C.LocalId;
		try
		{
			await OControle(eu);
			await OReflexoDaMente(eu);
			await ACopiaDoSplitform(eu);
			await OSegundoEscritorDeVolta(eu);
		}
		catch (Exception e)
		{
			Afirmar($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			Jandirus.Server.GameServer.LookSemTipoDeFusaoDeTeste = false;
			S.LimparOReflexoNoTeste(eu);
		}

		OsOutrosPacotes();
		Fechar();
	}

	/// <summary>
	/// A QUE DISTANCIA DO DONO O REFLEXO E POSTO antes de cada medida de pixel. Quatro tiles: longe o
	/// bastante pra ele nao alcancar o dono no quarto de segundo que a medida leva (o alcance do soco e
	/// um tile), e perto o bastante pra a caixa dele caber na foto com a camera em 3x e em 4x -- com
	/// seis ele parava a 64 px da beirada da tela.
	/// </summary>
	private const float TilesDeFolga = 4;

	/// <summary>Quantos `PeerLook` o cliente leu mal ate agora (lancou, ou passou do fim).</summary>
	private static int MalLidos() => C?.PacotesMalLidos(Protocol.S2C.PeerLook) ?? 0;

	// =====================================================================
	// 0. O CONTROLE
	// =====================================================================
	private async Task OControle(int eu)
	{
		GD.Print("[reflexo] --- 0. o preparo e o controle ---");

		if (S!.NaMenteNoTeste(eu))
		{
			Nota("este personagem logou DENTRO da mente; a bancada sai dela antes de comecar.");
			await SairDaMente(eu);
		}

		int pecas = S.VestirNoTeste(eu);
		await Esperar(0.8);

		var meu = World.Instancia?.LookDeTeste(eu);
		List<string> noCorpo = World.Instancia?.VisualLocalDeTeste?.RoupasNoCorpoDeTeste() ?? [];
		Afirmar($"(preparo) o corpo da bancada veste {pecas} pecas, e o proprio `PeerLook` as trouxe pro boneco na tela",
				pecas >= 2 && meu is { } m && m.Ap.Roupa.Count == pecas && noCorpo.Count == pecas,
				$"servidor {pecas}, ficha recebida {meu?.Ap.Roupa.Count ?? -1}, camadas no boneco {noCorpo.Count}");

		Afirmar("(controle) depois do login e da troca de roupa, NENHUM `PeerLook` desta sessao foi mal lido -- "
				+ "a regua nao reprova pacote bem escrito",
				MalLidos() == 0, $"{MalLidos()} leitura(s) quebrada(s) antes de qualquer corpo erguido");
	}

	// =====================================================================
	// 1. O REFLEXO DA MENTE
	// =====================================================================
	private async Task OReflexoDaMente(int eu)
	{
		GD.Print("[reflexo] --- 1. o reflexo da mente chega com a ficha do dono ---");

		int malAntes = MalLidos();
		if (!await EntrarNaMente(eu)) return;

		int id = S!.ReflexoNoTeste(eu);
		Afirmar("(preparo) a mente ergueu um reflexo (`EntrarNaMente` -> `CriarClone`, pelo pacote do jogador)", id != 0);
		if (id != 0)
			await JulgarOCorpoErguido(eu, id, malAntes, "o reflexo", $"{C!.LocalName} (mente)", "reflexo-1-a-mente",
									  // O reflexo LUTA, e um corpo colado no outro suja a medida de pixel dos
									  // dois: a bancada o afasta (o mesmo gatilho da `--diagmergulho`) e mede
									  // antes de ele voltar. A regra do reflexo nao muda -- so a distancia.
									  separar: () => S!.AfastarOReflexoNoTeste(eu, TilesDeFolga));

		await SairDaMente(eu);
	}

	// =====================================================================
	// 2. A COPIA DO SPLITFORM
	// =====================================================================
	private async Task ACopiaDoSplitform(int eu)
	{
		GD.Print("[reflexo] --- 2. a copia do Splitform chega com a ficha do dono ---");

		bool sabe = S!.EnsinarADivisaoNoTeste(eu);
		Afirmar("(preparo) o corpo da bancada sabe dividir o corpo (o portao `SabeTecnica` do verb)", sabe);
		if (!sabe) return;

		int malAntes = MalLidos();
		C!.SendHabilidade("SplitForm");
		bool nasceu = await EsperarAte(() => S.CopiasNoTeste(eu).Count > 0, 4.0);
		Afirmar("(preparo) o verb `SplitForm` ergueu uma copia (`UsarTecnica` -> `DividirOCorpoG12`, pelo pacote do jogador)", nasceu);
		if (!nasceu) return;

		int id = S.CopiasNoTeste(eu)[0];
		await JulgarOCorpoErguido(eu, id, malAntes, "a copia", $"{C.LocalName} Copy", "reflexo-2-a-copia", separar: null);

		// A SAIDA TAMBEM E A DO JOGADOR: o verb de menu que desfaz as copias.
		C.SendVerbo("splitform_destruir");
		await EsperarAte(() => S.CopiasNoTeste(eu).Count == 0, 3.0);
	}

	// =====================================================================
	// 3. O CONTRA-EXEMPLO
	// =====================================================================
	/// <summary>
	/// O SEGUNDO ESCRITOR DE VOLTA. Com o `GameServer.LookSemTipoDeFusaoDeTeste` ligado, o pacote do
	/// reflexo sai como saia ate 2026-10-08 -- e a regua da cena 1 tem que ficar vermelha.
	///
	/// O DEFEITO INJETADO TEM O ALCANCE DO DEFEITO DE VERDADE: so o reflexo e a copia saem sem o byte.
	/// O mergulho tambem apresenta o CORPO LARGADO do dono (outro corpo sem `Peer`, na zona de origem), e
	/// esse nunca passou pelo segundo escritor -- com o campo valendo pra todo corpo sem dono, o pacote
	/// dele ja acenderia o placar e a cena passaria sem que o do reflexo tivesse quebrado.
	///
	/// O QUE ACONTECE COM A FICHA NAO E AFIRMADO, so anotado: depende do buffer do pool (ver o
	/// cabecalho). Afirmar "a ficha se perde" seria uma prova que passa e reprova por sorte.
	/// </summary>
	private async Task OSegundoEscritorDeVolta(int eu)
	{
		GD.Print("[reflexo] --- 3. o contra-exemplo: o segundo escritor do `PeerLook` de volta ---");

		Jandirus.Server.GameServer.LookSemTipoDeFusaoDeTeste = true;
		try
		{
			int malAntes = MalLidos();
			if (!await EntrarNaMente(eu)) return;

			int id = S!.ReflexoNoTeste(eu);
			bool naTela = id != 0 && await EsperarAte(() => World.Instancia?.CorpoDeTeste(id) != null, 5.0);
			await Esperar(0.6);

			int mal = MalLidos() - malAntes;
			Afirmar("(defeito injetado: o `PeerLook` do reflexo sai sem o byte do tipo de fusao, como no segundo escritor) "
					+ "o cliente le o pacote ALEM do fim, ou estoura no meio -- a regua da cena 1 REPROVA",
					naTela && mal >= 1, $"reflexo #{id} na tela: {naTela}; leituras quebradas: {mal}");

			bool semFicha = World.Instancia?.LookDeTeste(id) == null;
			Nota(semFicha
				? "desta vez o buffer tinha o tamanho exato do pacote: o tratador ESTOUROU e o reflexo ficou SEM FICHA "
				  + "(sem nome, sem corpo, sem roupa -- o boneco nunca foi vestido)"
				: $"desta vez o buffer era maior que o pacote: o tratador nao estourou e leu a sobra de outro pacote como "
				  + $"tipo de fusao ({VisualDe(id)?.TipoDeFusaoDeTeste?.ToString() ?? "0, por sorte"}); a ficha chegou");

			if (naTela && Quadro() != null)
			{
				S.AfastarOReflexoNoTeste(eu, TilesDeFolga);
				await Quadros(8);
				int? px = await PixelsDoBoneco(id);
				Nota($"com o defeito, esconder o boneco do reflexo muda {px?.ToString() ?? "?"} px da tela");
				Fotografar("reflexo-3-o-defeito", "o reflexo com o segundo escritor de volta", eu, id);
			}
		}
		finally
		{
			Jandirus.Server.GameServer.LookSemTipoDeFusaoDeTeste = false;
			await SairDaMente(eu);
		}
	}

	// =====================================================================
	// O JUIZO DE UM CORPO ERGUIDO -- o mesmo pras cenas 1 e 2
	// =====================================================================
	private async Task JulgarOCorpoErguido(int eu, int id, int malAntes, string quem, string nomeEsperado,
										   string foto, Func<bool>? separar)
	{
		// O CORPO NASCE DO SNAPSHOT (canal nao-confiavel) e a ficha vem no canal confiavel: nao ha ordem
		// entre os dois. Espera-se o corpo, e depois um tempo que cobre com folga o atraso do outro canal.
		bool naTela = await EsperarAte(() => World.Instancia?.CorpoDeTeste(id) != null, 5.0);
		Afirmar($"(preparo) o corpo d{quem} (#{id}) entrou na tela pelo snapshot", naTela);
		if (!naTela) return;
		await Esperar(0.6);

		// ---- o FIO
		int mal = MalLidos() - malAntes;
		Afirmar($"o cliente leu o `PeerLook` d{quem} INTEIRO: sem lancar e sem passar do fim do pacote",
				mal == 0, $"{mal} leitura(s) quebrada(s) de `PeerLook` desde que a cena comecou");

		// ---- o MUNDO, contra a autoridade
		var chegou = World.Instancia?.LookDeTeste(id);
		var noServidor = S!.CorpoNoTeste(id);
		Afirmar($"a FICHA d{quem} chegou ao mundo do cliente", chegou != null,
				"o `_looks` nao tem este id: o boneco nasceu sem ter o que vestir");
		if (chegou is { } c && noServidor is { } s)
			Afirmar("...e e a que o corpo TEM no servidor: raca, genero, corpo, tom, cabelo, cores e cada peca com a cor",
					c.Raca == s.Raca && c.Genero == s.Genero && Resumo(c.Ap) == Resumo(s.Visual),
					$"chegou [{c.Raca}/{c.Genero} {Resumo(c.Ap)}] servidor [{s.Raca}/{s.Genero} {Resumo(s.Visual)}]");

		// ---- o DONO
		var meu = World.Instancia?.LookDeTeste(eu);
		if (chegou is { } c2 && meu is { } m)
			Afirmar($"...que e a do DONO: {quem} veste o que eu visto ({m.Ap.Roupa.Count} pecas e o cabelo {m.Ap.Cabelo})",
					m.Ap.Roupa.Count >= 2 && c2.Raca == m.Raca && c2.Genero == m.Genero && Resumo(c2.Ap) == Resumo(m.Ap),
					$"dele [{Resumo(c2.Ap)}] minha [{Resumo(m.Ap)}]");

		int idDoNome = World.Instancia?.IdPeloNome(nomeEsperado) ?? 0;
		Afirmar($"o NOME chegou junto: \"{nomeEsperado}\" e o corpo #{id} (o balao de fala e o alvo o acham por ai)",
				idDoNome == id, $"o nome resolve pro corpo #{idDoNome}");

		CharacterVisual? dele = VisualDe(id), eutela = World.Instancia?.VisualLocalDeTeste;
		Afirmar($"{quem} NAO e fusao nenhuma: o byte do tipo chegou 0, e nao a sobra de outro pacote",
				dele != null && dele.TipoDeFusaoDeTeste == null, $"tipo de fusao no boneco: {dele?.TipoDeFusaoDeTeste}");

		// ---- as CAMADAS
		List<string> pecasDele = dele?.RoupasNoCorpoDeTeste() ?? [], pecasMinhas = eutela?.RoupasNoCorpoDeTeste() ?? [];
		Afirmar($"o boneco d{quem} foi MONTADO como o meu: a mesma folha de corpo, as mesmas pecas na mesma ordem, o mesmo cabelo",
				dele != null && eutela != null
				&& dele.FolhaDoCorpoDeTeste.Length > 0 && dele.FolhaDoCorpoDeTeste == eutela.FolhaDoCorpoDeTeste
				&& pecasMinhas.Count >= 2 && pecasDele.SequenceEqual(pecasMinhas)
				&& dele.TemCabeloDeTeste && dele.CabeloDeTeste == eutela.CabeloDeTeste,
				$"dele: corpo '{Arquivo(dele?.FolhaDoCorpoDeTeste)}' roupa [{string.Join(" + ", pecasDele.Select(Arquivo))}] cabelo '{Arquivo(dele?.CabeloDeTeste)}'"
				+ $" | meu: corpo '{Arquivo(eutela?.FolhaDoCorpoDeTeste)}' roupa [{string.Join(" + ", pecasMinhas.Select(Arquivo))}] cabelo '{Arquivo(eutela?.CabeloDeTeste)}'");

		// ---- o PIXEL
		string oPixel = $"{quem} DESENHA corpo na tela (esconder o boneco muda pixel)";
		if (Quadro() == null) { NaoMediu(oPixel, "headless nao renderiza; rode com janela"); return; }

		separar?.Invoke();
		await Quadros(8);   // o corpo remoto e interpolado: da tempo de o desenho chegar onde o servidor o pos
		int? pxDele = await PixelsDoBoneco(id);
		Fotografar(foto, $"{quem} ao lado do dono", eu, id);
		separar?.Invoke();
		await Quadros(8);
		int? pxMeus = await PixelsDoBoneco(eu);

		// SEM NUMERO NAO HA VEREDITO: um corpo cuja caixa saiu da foto (ou que sumiu no meio da medida)
		// daria uma contagem cortada, e uma contagem cortada reprova um corpo que esta desenhado inteiro.
		if (pxDele is not { } d || pxMeus is not { } me)
		{
			NaoMediu(oPixel, "um dos dois corpos nao coube inteiro na foto, ou sumiu no meio da medida");
			return;
		}
		Afirmar($"{quem} DESENHA corpo na tela: esconder o boneco dele muda {d} px, e esconder o MEU (a mesma ficha) muda {me} px",
				me >= PisoDeUmCorpo && d >= me / 2,
				$"piso de um corpo: {PisoDeUmCorpo} px; o dele tem que dar pelo menos metade do meu");
	}

	/// <summary>
	/// UMA FICHA EM UMA LINHA, pra comparar e pra ler no log. Entra TUDO o que o pacote carrega -- uma
	/// comparacao campo a campo escrita a mao aqui seria a lista que esquece o campo novo.
	/// </summary>
	private static string Resumo(Appearance a) =>
		$"corpo {a.Corpo} tom {a.Tom} pele {a.CorPele} cabelo {a.Cabelo} {a.CorCabelo} olho {a.CorOlho} "
		+ $"roupa [{string.Join(" + ", a.Roupa.Select(p => $"{Arquivo(p.Caminho)}{p.Cor}"))}] "
		+ $"frost [{string.Join(",", a.FormasDeFrost)}] aura {a.CorAura} ki {a.CorKi}";

	private static string Arquivo(string? caminho) =>
		string.IsNullOrEmpty(caminho) ? "" : System.IO.Path.GetFileNameWithoutExtension(caminho);

	private static CharacterVisual? VisualDe(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	// =====================================================================
	// A PORTA DA MENTE
	// =====================================================================
	/// <summary>Meditar e mergulhar -- os dois pacotes que a telinha manda. Ver "os atalhos" no cabecalho.</summary>
	private async Task<bool> EntrarNaMente(int eu)
	{
		C!.SendActivity(Protocol.Activity.Meditando);
		await Esperar(0.3);
		C.SendHabilidade("mente");

		// A ONDA VEM ANTES DA VIAGEM (`DimensaoMental.MsDaOnda`), entao a espera cobre as duas.
		bool dentro = await EsperarAte(() => S!.NaMenteNoTeste(eu), 8.0);
		if (!dentro) Afirmar("(preparo) o corpo chegou na mente (meditar + mergulhar)", false,
							 "o servidor nao pos o corpo na mente em 8 s");
		return dentro;
	}

	private async Task SairDaMente(int eu)
	{
		if (S!.NaMenteNoTeste(eu))
		{
			C!.SendHabilidade("sairdamente");
			await EsperarAte(() => !S.NaMenteNoTeste(eu), 5.0);
		}
		C!.SendActivity(Protocol.Activity.Parado);
		await Esperar(0.8);
	}

	// =====================================================================
	// PIXEL
	// =====================================================================
	private Image? Quadro()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>
	/// O MINIMO QUE UM CORPO VESTIDO MUDA NA TELA. Um boneco tem 32x32 px de arte e a camera nunca fica
	/// abaixo de 1x: uma centena de pixels e bem menos que qualquer corpo e bem mais que o ruido de um
	/// quadro parado. Ele so existe pra a comparacao "metade do meu" nao passar com os dois em zero.
	/// </summary>
	private const int PisoDeUmCorpo = 100;

	/// <summary>Soma dos tres canais, 0 a 3 -- a mesma distancia (e o mesmo limiar) da `--diagcarga`.</summary>
	private const double MudouDeVerdade = 0.09;

	/// <summary>
	/// QUANTOS PIXELS DA TELA O BONECO DESTE CORPO DESENHA: a diferenca entre o quadro com o
	/// `CharacterVisual` escondido e o quadro com ele de volta, numa caixa em volta do corpo.
	///
	/// ESCONDE O `Visual` E NAO O CORPO: o `World` reescreve o `Visible` do corpo remoto a cada snapshot
	/// (a regra de quem voa alto), e o quadro "sem corpo" sairia com ele. O `Visual` ninguem reescreve --
	/// e ele e exatamente o que a ficha veste. Aura, sombra e balao ficam de fora da conta de proposito.
	///
	/// NAO DEPENDE DO FUNDO: a mente e branca e a Terra e grama, e a mesma regua mede as duas.
	/// </summary>
	private async Task<int?> PixelsDoBoneco(int id)
	{
		Node2D? corpo = World.Instancia?.CorpoDeTeste(id);
		CharacterVisual? v = corpo?.GetNodeOrNull<CharacterVisual>("Visual");
		if (corpo == null || v == null) return null;

		Image? sem, com;
		try
		{
			v.Visible = false;
			await Quadros(2);
			sem = Quadro();
		}
		finally
		{
			if (IsInstanceValid(v)) v.Visible = true;
		}
		await Quadros(2);
		com = Quadro();
		if (sem == null || com == null || !IsInstanceValid(corpo)) return null;

		Rect2I caixa = CaixaNaFoto(corpo, com, 30, 36, out bool inteira);
		if (!inteira) return null;

		int mudaram = 0;
		for (int y = caixa.Position.Y; y < caixa.End.Y; y++)
			for (int x = caixa.Position.X; x < caixa.End.X; x++)
			{
				Color a = sem.GetPixel(x, y), b = com.GetPixel(x, y);
				if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > MudouDeVerdade) mudaram++;
			}
		return mudaram;
	}

	/// <summary>
	/// A CAIXA DE UM CORPO NA FOTO, em pixels da imagem. `GetGlobalTransformWithCanvas` ja carrega o zoom
	/// da camera; o que falta e a escala da JANELA (o projeto estica o canvas: a foto tem o tamanho da
	/// janela e o canvas o tamanho de projeto), que sai da razao entre os dois.
	///
	/// <paramref name="inteira"/> DIZ SE ELA COUBE NA FOTO. A caixa volta cortada na borda da imagem, e
	/// quem CONTA pixel nela precisa saber: a primeira rodada desta bancada afastou o reflexo seis tiles
	/// e ele parou a 64 px da beirada esquerda da tela -- coube por pouco, e noutro zoom nao caberia.
	/// </summary>
	private static Rect2I CaixaNaFoto(Node2D corpo, Image img, float meiaLargura, float meiaAltura, out bool inteira)
	{
		Transform2D t = corpo.GetGlobalTransformWithCanvas();
		Vector2 visivel = corpo.GetViewportRect().Size;
		var janela = new Vector2(img.GetWidth() / Math.Max(1f, visivel.X), img.GetHeight() / Math.Max(1f, visivel.Y));
		Vector2 centro = t.Origin * janela;
		Vector2 meio = new Vector2(meiaLargura * t.Scale.X, meiaAltura * t.Scale.Y) * janela;

		var bruta = new Rect2I((int)(centro.X - meio.X), (int)(centro.Y - meio.Y), (int)(meio.X * 2), (int)(meio.Y * 2));
		Rect2I cortada = bruta.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
		inteira = cortada == bruta;
		return cortada;
	}

	/// <summary>
	/// A FOTO INTEIRA e DOIS RECORTES ampliados, um em volta do corpo erguido (`-corpo`) e um em volta
	/// do dono (`-dono`): a 1x o boneco tem poucos pixels de tela, e ninguem ve roupa nenhuma numa foto
	/// de 1280 de largura. Os dois lado a lado sao a comparacao que o olho faz.
	/// </summary>
	private void Fotografar(string nome, string rotulo, int eu, int outro)
	{
		if (Quadro() is not { } img) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		img.SavePng($"user://{nome}.png");
		Nota($"foto: {ProjectSettings.GlobalizePath($"user://{nome}.png")}  ({rotulo})");
		Recortar(img, $"{nome}-corpo", outro);
		Recortar(img, $"{nome}-dono", eu);
	}

	private void Recortar(Image img, string nome, int id)
	{
		if (World.Instancia?.CorpoDeTeste(id) is not { } corpo) return;
		Rect2I caixa = CaixaNaFoto(corpo, img, 40, 48, out _);   // o recorte e pra o olho: cortado na borda ainda serve
		if (caixa.Size.X < 8 || caixa.Size.Y < 8) return;

		Image recorte = img.GetRegion(caixa);
		int vezes = Math.Clamp(640 / Math.Max(1, caixa.Size.X), 1, 8);
		recorte.Resize(caixa.Size.X * vezes, caixa.Size.Y * vezes, Image.Interpolation.Nearest);
		recorte.SavePng($"user://{nome}.png");
		Nota($"  recorte {vezes}x: {ProjectSettings.GlobalizePath($"user://{nome}.png")}");
	}

	// =====================================================================
	// FIM
	// =====================================================================
	/// <summary>
	/// O QUE MAIS O CLIENTE LEU MAL NESTA RODADA, fora o `PeerLook`. NOTA e nao afirmacao: esta bancada
	/// nao e dona dos outros pacotes -- mas a regua e geral, a rodada passou por login, duas trocas de
	/// zona e uma tecnica, e um desencontro de layout que ela tenha visto no caminho merece uma linha.
	/// </summary>
	private void OsOutrosPacotes()
	{
		if (C is not { } cli) return;
		var outros = new List<string>();
		foreach (Protocol.S2C op in Enum.GetValues<Protocol.S2C>())
			if (op != Protocol.S2C.PeerLook && cli.PacotesMalLidos(op) is > 0 and int n) outros.Add($"{op} x{n}");
		Nota(outros.Count == 0
			? "nenhum OUTRO pacote do servidor foi mal lido nesta rodada"
			: $"ATENCAO -- outros pacotes mal lidos nesta rodada (nao sao desta bancada): {string.Join(", ", outros)}");
	}

	private void Fechar()
	{
		_fechou = true;
		GD.Print("");
		GD.Print("========== BANCADA DA FICHA DO CORPO ERGUIDO ==========");
		foreach (string l in _linhas) GD.Print(l);
		GD.Print($"===== FIM: {_ok} OK, {_falhou} FALHA(S), {_semMedida} SEM MEDIDA =====");
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(_falhou == 0 ? 0 : 1);
	}
}
