using System.Collections;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO BILLS (`--diagbills`) -- a ponta da TELA do pedido do dono de 2026-10-11: os NPCs Bills e Whis.
///
/// A regra inteira (quem existe, o calendario, os 15%, o triplo, a maestria, o titulo, o traje, o sigilo do poder)
/// e medida pela bancada do servidor (`--billsteste`). O que ela nao ve e o que so a tela tem: se o Bills e
/// desenhado com a folha dele E DE TRAJE, se o Whis esta ao lado, se o endereco falado chega no chat e marca a
/// carta estelar, se em casa os dois ficam PARADOS como objeto, se a tecla E acende a dica e abre o menu de cada
/// um, se o treino comeca e para pelos botoes, se o Instinto do Whis nao pinta nada na tela e se a Fury do Bills
/// pinta.
///
/// TUDO PELA ENTRADA DE VERDADE: a tecla E e empurrada no viewport, os botoes sao apertados pelo sinal, e o que
/// se afirma e lido de DOIS lados -- o servidor do mesmo processo (modo host) e a tela. O palco
/// (`Server/GameServer.BillsPalco.cs`) so pula o que e ESPERA: o mes, o soco que derruba e a viagem ate a casa.
///
/// AS FOTOS saem em `user://bills-NN-*.png`: a visita (o Bills de traje com o Whis ao lado), a derrota (a fala
/// do endereco), a carta estelar com a casa marcada, os dois parados no planeta, o menu de cada um, o treino, o
/// menu no meio do treino e o desafio com a Fury de pe.
///
/// Rode com janela (no segundo monitor), no modo host, com um Humano (ele nasce na Terra, que e onde a visita e):
///   --host --rede 8331 --horateste 0.5 --diagbills --raca Human --conta bancada_bills_d --nome Fotografo
/// </summary>
public partial class RoboDeBills : Node
{
	private const int Tile = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];
	private readonly List<string> _falas = [];
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dbills]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dbills]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[dbills] " + s);

	public override void _Ready()
	{
		if (C is { } cli) cli.Falou += AoFalar;
	}

	public override void _ExitTree()
	{
		// O `-=` DO PAR: o `GameClient` sobrevive ao relog e este node nao.
		if (C is { } cli) cli.Falou -= AoFalar;
	}

	private void AoFalar(Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private bool Disse(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(4).Select(f => f.Length > 90 ? f[..90] + "..." : f));

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		// O PLACAR SAI DE QUALQUER JEITO: um passo do roteiro que estoura vira uma vermelha e encerra a rodada.
		_roteiro ??= Roteiro().GetEnumerator();
		bool segue;
		try { segue = _roteiro.MoveNext(); }
		catch (Exception e) { Afirmar("(o roteiro chegou ao fim sem estourar)", false, e.GetType().Name + ": " + e.Message); segue = false; }
		if (!segue) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		MenuDeInteracao.EscSoDepoisDaPausaDeTeste = false;
		S?.RecolherOPalcoDoBillsDeTeste();
		GD.Print($"\n[dbills] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[dbills] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	private void Tecla(Key k) =>
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
	}

	private static CharacterVisual? VisualDe(int id) =>
		M?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private static T? Procurar<T>(Node raiz, Func<T, bool> quer) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t && quer(t)) return t;
			if (Procurar(f, quer) is { } achado) return achado;
		}
		return null;
	}

	/// <summary>Um numero comprido (seis algarismos ou mais, com ou sem ponto de milhar) numa fala: seria um PODER escrito.</summary>
	private static bool TemNumeroDePoder(string fala) =>
		System.Text.RegularExpressions.Regex.IsMatch(fala, @"\d[\d\.,]{5,}|trilh|bilh");

	/// <summary>A forma do corpo NA TELA: ha camada de corpo de forma visivel, aura colada ou contorno de forma?</summary>
	private static bool TemEfeitoDeForma(CharacterVisual v) =>
		v.CorpoDaFormaVisivelDeTeste || v.ColadasDeTeste > 0 || v.AuraDaFormaDeTeste > 0.001f;

	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		World mundo = M!;
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") < 0 || S is not { } srv)
		{
			Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false);
			yield break;
		}
		int eu = cli.LocalId;
		string folhaDoBills = BillsEWhis.FolhaDoCorpo(BillsEWhis.RacaDoBills) ?? "";
		string folhaDoWhis = BillsEWhis.FolhaDoCorpo(BillsEWhis.RacaDoWhis) ?? "";

		// =====================================================================
		// 1) A VISITA A TERRA
		// =====================================================================
		Nota("-- 1) a visita a Terra: o Bills de traje, o Whis ao lado --");
		Afirmar("antes de tudo ninguem conhece a casa: o cliente nao tem endereco, e a carta nao marca mundo nenhum",
				!cli.CasaDeBills.Revelada && MenuJogo.MundoDaCasaDeBills() == null);

		ZoneKey terra = cli.Zone;
		(int Bills, int Whis)? pousados = srv.BillsNaTerraDeTeste(eu);
		Afirmar("(montagem) o calendario de producao pousou os dois na Terra, e o host esta ao lado", pousados != null, $"zona do host: {cli.Zone.Name}");
		if (pousados is not { } naTerra) yield break;

		for (int i = 0; i < 60 && (mundo.CorpoDeTeste(naTerra.Bills) == null || mundo.CorpoDeTeste(naTerra.Whis) == null); i++)
			yield return 0.25;
		srv.CravarMeioDiaDaVariedade(eu);
		yield return 2.0;

		CharacterVisual? vb = VisualDe(naTerra.Bills), vw = VisualDe(naTerra.Whis);
		Afirmar("os dois corpos estao NA TELA", vb != null && vw != null);
		if (vb == null || vw == null) yield break;

		Afirmar("**O BILLS E DESENHADO COM A FOLHA DELE** (`Beerus (God of Destruction)`), sem cabelo nem olhos de gente por cima",
				vb.FolhaDoCorpoDeTeste == folhaDoBills && !vb.TemCabeloVisivelDeTeste && !vb.OlhosVisiveisDeTeste,
				$"folha '{vb.FolhaDoCorpoDeTeste}', cabelo={vb.TemCabeloVisivelDeTeste}, olhos={vb.OlhosVisiveisDeTeste}");
		List<string> roupaDoBills = vb.RoupasNoCorpoDeTeste();
		Afirmar("**ELE ESTA DE TRAJE**: a folha dele vem sem roupa, e a UNICA peca no corpo e a `Beerus Inspired Suit (DU)`",
				roupaDoBills.Count == 1 && roupaDoBills[0].Contains(BillsEWhis.Traje, StringComparison.Ordinal),
				roupaDoBills.Count == 0 ? "sem roupa nenhuma" : string.Join(", ", roupaDoBills));
		Afirmar("**O WHIS E DESENHADO COM A FOLHA DELE**, sem peca por cima (a folha ja vem vestida), sem cabelo nem olhos de gente",
				vw.FolhaDoCorpoDeTeste == folhaDoWhis && vw.RoupasNoCorpoDeTeste().Count == 0 && !vw.TemCabeloVisivelDeTeste && !vw.OlhosVisiveisDeTeste,
				$"folha '{vw.FolhaDoCorpoDeTeste}', {vw.RoupasNoCorpoDeTeste().Count} peca(s)");
		string folhaDoHost = VisualDe(eu)?.FolhaDoCorpoDeTeste ?? "";
		Afirmar("(controle da regua) o host, que e gente, e desenhado com OUTRA folha",
				folhaDoHost.Length > 0 && folhaDoHost != folhaDoBills && folhaDoHost != folhaDoWhis, folhaDoHost);

		Vector2 pb = mundo.PosicaoDesenhadaDe(naTerra.Bills) ?? Vector2.Zero, pw = mundo.PosicaoDesenhadaDe(naTerra.Whis) ?? Vector2.Inf;
		Afirmar("**O WHIS ESTA AO LADO DO BILLS**: a poucos tiles, na mesma tela",
				pb.DistanceTo(pw) <= (BillsEWhis.TilesEntreOsDois + 1.5f) * Tile, $"{pb.DistanceTo(pw) / Tile:0.0} tiles");
		// ENCOSTADO NO BILLS, ao alcance da tecla E: na Terra ele nao tem menu (ele cochila, e so acorda apanhando).
		bool encostou = srv.AoLadoDoDivinoDeTeste(eu, whis: false);
		yield return 1.0;
		Afirmar("na Terra nenhum dos dois tem menu: ENCOSTADO no Bills, a tecla E nao o oferece (la a conversa e no soco)",
				encostou && cli.DivinoPerto == 0 && !(E?.DicaNaTela ?? "").Contains("Bills", StringComparison.Ordinal) && !(E?.DicaNaTela ?? "").Contains("Whis", StringComparison.Ordinal),
				$"encostou={encostou}, dica '{E?.DicaNaTela}'");

		// A FALA DE CHEGADA INTEIRA (quatro linhas, uma a cada tres segundos e meio), pro chat e pra foto.
		for (int i = 0; i < 80 && !Disse(BillsEWhis.FalasDaChegada[^1].Texto); i++) yield return 0.25;
		Afirmar("os dois FALAM ao chegar: as quatro linhas da chegada chegaram no chat de quem esta perto",
				BillsEWhis.FalasDaChegada.All(f => Disse(f.Texto)), Ultimas());
		Fotografar("bills-01-a-visita-o-bills-de-traje-e-o-whis");

		// =====================================================================
		// 2) A DERROTA: ELE FALA O ENDERECO
		// =====================================================================
		Nota("-- 2) derrotado, ele conta onde mora --");
		(int Sx, int Sy, int K, ulong Semente, double Gravidade, string Falado)? casa = srv.CasaDoBillsDeTeste();
		Afirmar("(montagem) o universo desta bancada tem casa pro Bills", casa != null);
		if (casa is not { } la) yield break;
		Nota($"a casa: {la.Falado}, gravidade {la.Gravidade:0}x");

		_falas.Clear();
		Afirmar("(montagem) o Bills caiu na Terra, e a queda e do host", srv.DerrubarOBillsNaTerraDeTeste(eu));
		bool falou = false;
		for (int i = 0; i < 120 && !falou; i++)
		{
			yield return 0.25;
			falou = Disse(la.Falado);
		}
		Afirmar("**DERROTADO, ELE FALA AS COORDENADAS**: o chat de quem esta perto le o sistema e a orbita, do jeito que a carta escreve",
				falou, Ultimas());
		Fotografar("bills-02-derrotado-ele-fala-o-endereco");

		for (int i = 0; i < 240 && srv.EstadoDoBillsDeTeste() is { EmCena: true }; i++) yield return 0.25;
		yield return 2.0;
		Afirmar("a historia virou \"em casa\", e os dois FORAM EMBORA da Terra: sairam da tela",
				srv.EstadoDoBillsDeTeste().Fase == FaseDaVisita.EmCasa && mundo.CorpoDeTeste(naTerra.Bills) == null && mundo.CorpoDeTeste(naTerra.Whis) == null,
				$"fase {srv.EstadoDoBillsDeTeste().Fase}");
		string comPoder = _falas.Find(TemNumeroDePoder) ?? "";
		Afirmar("NENHUMA fala da visita escreve um numero de poder (a regra principal do BP): so o endereco tem algarismo",
				_falas.Count >= BillsEWhis.FalasDaDerrota.Length && comPoder.Length == 0, comPoder);

		GameClient.CasaDeBillsVista vista = cli.CasaDeBills;
		Afirmar("**A CASA CHEGOU PELO FIO**: o mesmo sistema, a mesma orbita e a mesma semente que o servidor tem",
				vista.Revelada && vista.Sx == la.Sx && vista.Sy == la.Sy && vista.Orbita == la.K && vista.Semente == la.Semente,
				$"cliente {vista.Sx}:{vista.Sy} #{vista.Orbita}, servidor {la.Sx}:{la.Sy} #{la.K}");
		PlanetaNoEspaco? marcado = MenuJogo.MundoDaCasaDeBills();
		Afirmar("**A CARTA ESTELAR A RECONHECE SOZINHA**: o cliente enumera aquele sistema pela semente do universo e acha o mundo daquela orbita -- e a marca e so dele (nenhum dos mundos de mapa proprio e marcado)",
				marcado is { } m && m.Seed == la.Semente && MenuJogo.CasaDeBills(m) && !Espaco.PreFeitos().Any(MenuJogo.CasaDeBills),
				marcado is { } m2 ? m2.Nome : "a carta nao achou o mundo");

		// a carta, na tela: o Nav System na mochila, a aba, o botao que leva ate la
		srv.NavSystemNaMochilaDeTeste(eu, true);
		for (int i = 0; i < 32 && !cli.Atributos.Tem(Protocol.Poder.Nav); i++) yield return 0.25;
		MenuJogo? menuP = MenuJogo.Instancia;
		menuP?.Abrir();
		menuP?.IrPara("Nav");
		yield return 1.0;
		Control? paginaNav = menuP?.PaginaDeTeste("Nav");
		Button? irACasa = paginaNav == null ? null : Procurar<Button>(paginaNav, b => b.Text == "casa de Bills");
		Afirmar("a aba Nav ganhou o botao \"casa de Bills\" (ele so existe depois da revelacao)", irACasa != null);
		irACasa?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 1.5;
		MapaEstelar? mapa = menuP?.MapaDeTeste;
		Afirmar("...e o botao leva a carta ate o mundo e o SELECIONA: ele esta no enquadramento, e o destino do painel e a casa",
				mapa?.Selecionado is { } sel && sel.Seed == la.Semente && mapa.PlanetasDeTeste().Exists(p => p.Seed == la.Semente),
				mapa?.Selecionado is { } s2 ? s2.Nome : "nada selecionado");
		Fotografar("bills-03-a-casa-na-carta-estelar");
		menuP?.Fechar();
		yield return 0.5;

		// =====================================================================
		// 3) A CASA: PARADOS, COMO OBJETO
		// =====================================================================
		Nota("-- 3) a casa: os dois parados, a tecla E e o menu de cada um --");
		_falas.Clear();
		(int Bills, int Whis)? emCasa = srv.PousarNaCasaDoBillsDeTeste(eu);
		Afirmar("(montagem) o host pousou no mundo deles pelo pouso de producao, e os dois estao la", emCasa != null, $"zona do host: {cli.Zone.Name}");
		if (emCasa is not { } ids) yield break;

		for (int i = 0; i < 320 && (mundo.ZonaMontadaDeTeste != cli.Zone.Hash || mundo.Colisao == null || cli.Zone.Hash == terra.Hash || Espaco.EhEspaco(cli.Zone)); i++)
			yield return 0.25;
		srv.CravarMeioDiaDaVariedade(eu);
		// DOIS TILES E MEIO AO SUL DO WHIS: os dois cabem na tela, e o host ainda esta FORA do alcance da tecla E.
		bool longe = srv.AoLadoDoDivinoDeTeste(eu, whis: true, tiles: 2.5f);
		for (int i = 0; i < 60 && (mundo.CorpoDeTeste(ids.Bills) == null || mundo.CorpoDeTeste(ids.Whis) == null); i++)
			yield return 0.25;
		yield return 2.5;
		Nota($"pousou em {cli.Zone.Name}");
		Afirmar("ao pousar no mundo deles o jogador le o RUMO: pra que lado, e a quantos passos, os dois estao",
				Disse(BillsEWhis.RumoDosDois[..BillsEWhis.RumoDosDois.IndexOf('{')]), Ultimas());

		vb = VisualDe(ids.Bills);
		vw = VisualDe(ids.Whis);
		Afirmar("em casa os dois estao NA TELA, com a mesma cara da visita: o Bills de traje, o Whis na folha dele",
				vb != null && vw != null && vb.FolhaDoCorpoDeTeste == folhaDoBills && vw.FolhaDoCorpoDeTeste == folhaDoWhis
				&& vb.RoupasNoCorpoDeTeste().Exists(r => r.Contains(BillsEWhis.Traje, StringComparison.Ordinal)));
		if (vb == null || vw == null) yield break;

		Vector2 postoDoBills = mundo.PosicaoDesenhadaDe(ids.Bills) ?? Vector2.Zero, postoDoWhis = mundo.PosicaoDesenhadaDe(ids.Whis) ?? Vector2.Zero;
		float andouB = 0, andouW = 0;
		for (int i = 0; i < 12; i++)
		{
			yield return 0.25;
			andouB = Math.Max(andouB, (mundo.PosicaoDesenhadaDe(ids.Bills) ?? postoDoBills).DistanceTo(postoDoBills));
			andouW = Math.Max(andouW, (mundo.PosicaoDesenhadaDe(ids.Whis) ?? postoDoWhis).DistanceTo(postoDoWhis));
		}
		Afirmar("**ELES FICAM PARADOS, COMO OBJETO**: em tres segundos nenhum dos dois saiu do lugar -- e continuam lado a lado",
				andouB < 1f && andouW < 1f && postoDoBills.DistanceTo(postoDoWhis) <= (BillsEWhis.TilesEntreOsDois + 1.5f) * Tile
				&& srv.DivinoDeTeste(whis: true) is { Modo: "Parado" } && srv.DivinoDeTeste(whis: false) is { Modo: "Parado" },
				$"Bills andou {andouB:0.0} px, Whis {andouW:0.0} px");
		// (a dica pode estar acesa por OUTRA coisa -- uma macieira ao lado tambem e alvo da tecla E; o que nao pode e ser deles)
		Afirmar("a dois tiles e meio a tecla E ainda nao os alcanca: o servidor nao aponta nenhum, e a dica nao fala deles (o controle do \"perto\")",
				longe && cli.DivinoPerto == 0 && !(E?.DicaNaTela ?? "").Contains("Bills", StringComparison.Ordinal) && !(E?.DicaNaTela ?? "").Contains("Whis", StringComparison.Ordinal),
				$"posto de longe={longe}, dica '{E?.DicaNaTela}'");
		Fotografar("bills-04-os-dois-parados-em-casa");

		// ---- o menu do Whis ----
		srv.AoLadoDoDivinoDeTeste(eu, whis: true);
		for (int i = 0; i < 16 && E?.DicaNaTela != "[E] Whis"; i++) yield return 0.25;
		Afirmar("ao lado do Whis a dica do pe da tela ensina a tecla: \"[E] Whis\"", E?.DicaNaTela == "[E] Whis", E?.DicaNaTela ?? "");
		Tecla(Key.E);
		yield return 0.5;
		Afirmar("**A TECLA E ABRE O MENU DO WHIS**, com UMA opcao: treinar",
				E is { NaTela: true } && E.TituloDesenhado == "Whis" && E.BotoesDesenhados().SequenceEqual(["Treinar com o Whis"]),
				E != null ? $"{E.TituloDesenhado}: {string.Join(" | ", E.BotoesDesenhados())}" : "");
		Fotografar("bills-05-o-menu-do-whis");
		Tecla(Key.E);
		yield return 0.4;
		Afirmar("a mesma tecla E FECHA o menu", E?.NaTela != true);

		// ---- O ESC COM O MENU ABERTO E DO MENU, e nao da pausa (o botao diz "Fechar (Esc)") ----
		// A primeira rodada desta bancada fotografou a PAUSA aberta em cima do menu do Bills: o menu de pausa
		// recebia o Esc antes do menu de interacao. Ver `MenuDeInteracao._Input`.
		PauseMenu? pausa = PauseMenu.Instancia;
		Tecla(Key.E);
		yield return 0.4;
		Afirmar("(montagem) o menu do Whis esta aberto de novo, e ha uma tela de pausa de producao na arvore (fechada)",
				E is { NaTela: true } && pausa is { Aberto: false });
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("**ESC COM O MENU DO WHIS ABERTO FECHA O MENU** -- e a pausa NAO abre",
				E?.NaTela != true && pausa is { Aberto: false }, $"menu={E?.NaTela}, pausa={pausa?.Aberto}");
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("...e o Esc SEGUINTE, sem menu nenhum na frente, abre a pausa como sempre", pausa is { Aberto: true });
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("...que o Esc fecha de volta", pausa is { Aberto: false });

		Tecla(Key.E);
		yield return 0.4;
		MenuDeInteracao.EscSoDepoisDaPausaDeTeste = true;
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("(defeito injetado: o Esc do menu so depois da pausa) a PAUSA abre por cima e o menu do Whis continua aberto atras -- a afirmacao de cima reprova",
				E?.NaTela == true && pausa is { Aberto: true }, $"menu={E?.NaTela}, pausa={pausa?.Aberto}");
		MenuDeInteracao.EscSoDepoisDaPausaDeTeste = false;
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("(sem o defeito) com a pausa na frente o Esc e DELA: ela fecha, e o menu de tras fica",
				E?.NaTela == true && pausa is { Aberto: false }, $"menu={E?.NaTela}, pausa={pausa?.Aberto}");
		Tecla(Key.Escape);
		yield return 0.4;
		Afirmar("...e o Esc seguinte fecha o menu", E?.NaTela != true && pausa is { Aberto: false });

		// ---- o menu do Bills ----
		srv.AoLadoDoDivinoDeTeste(eu, whis: false);
		for (int i = 0; i < 16 && E?.DicaNaTela != "[E] Bills"; i++) yield return 0.25;
		Afirmar("ao lado do Bills a dica troca: \"[E] Bills\" (o mais perto ganha)", E?.DicaNaTela == "[E] Bills", E?.DicaNaTela ?? "");
		Tecla(Key.E);
		yield return 0.5;
		Afirmar("**A TECLA E ABRE O MENU DO BILLS**, com DUAS opcoes: treinar e desafiar pelo titulo",
				E is { NaTela: true } && E.TituloDesenhado == "Bills" && E.BotoesDesenhados().SequenceEqual(["Treinar com o Bills", "Desafiar pelo título"]),
				E != null ? $"{E.TituloDesenhado}: {string.Join(" | ", E.BotoesDesenhados())}" : "");
		Fotografar("bills-06-o-menu-do-bills");
		Tecla(Key.E);
		yield return 0.4;

		// =====================================================================
		// 4) O TREINO COM O WHIS, PELO BOTAO
		// =====================================================================
		Nota("-- 4) o treino com o Whis: comeca e para pelo menu, e o Instinto dele nao pinta nada --");
		srv.AoLadoDoDivinoDeTeste(eu, whis: true);
		for (int i = 0; i < 16 && E?.DicaNaTela != "[E] Whis"; i++) yield return 0.25;
		Tecla(Key.E);
		yield return 0.4;
		_falas.Clear();
		bool apertou = E?.ApertarDesenhado("Treinar com o Whis") == true;
		float mexeuW = 0;
		for (int i = 0; i < 12; i++)
		{
			yield return 0.25;
			srv.ManterOHostDePeDeTeste(eu);
			mexeuW = Math.Max(mexeuW, (mundo.PosicaoDesenhadaDe(ids.Whis) ?? postoDoWhis).DistanceTo(postoDoWhis));
		}
		var treino = srv.DivinoDeTeste(whis: true);
		Afirmar("**O TREINO COMECA PELO BOTAO**: no servidor o Whis luta com o host (treino, e nao desafio), com o Instinto ligado -- e o menu fechou",
				apertou && treino is { Parceiro: > 0, Desafio: false, Lutando: true, Disciplina: true } t1 && t1.Parceiro == eu && E?.NaTela != true,
				treino is { } t2 ? $"modo {t2.Modo}, parceiro {t2.Parceiro}" : "o Whis sumiu");
		Afirmar("...e NA TELA ele saiu do posto: o que estava parado agora luta", mexeuW > 4f, $"{mexeuW:0.0} px do posto");
		vw = VisualDe(ids.Whis);
		Afirmar("**O INSTINTO DO WHIS NAO TEM EFEITO VISUAL**: lutando, ele continua na folha dele -- sem corpo de forma, sem aura colada, sem contorno de forma",
				vw != null && vw.FolhaDoCorpoDeTeste == folhaDoWhis && !TemEfeitoDeForma(vw),
				vw == null ? "sem corpo" : $"corpo de forma={vw.CorpoDaFormaVisivelDeTeste}, coladas={vw.ColadasDeTeste}, contorno={vw.AuraDaFormaDeTeste:0.00}");
		Afirmar("o Whis FALA ao comecar (a linha dele chegou no chat)", Disse(BillsEWhis.WhisComecaOTreino) || Disse(BillsEWhis.WhisComecaComInstinto), Ultimas());
		Fotografar("bills-07-o-treino-com-o-whis");

		// o menu no meio do treino: a saida -- A QUALQUER DISTANCIA (o Whis tambem luta de longe), e na frente do Bills ali ao lado
		bool dica = false;
		for (int i = 0; i < 24 && !dica; i++)
		{
			yield return 0.2;
			srv.ManterOHostDePeDeTeste(eu);
			dica = E?.DicaNaTela == "[E] Whis";
		}
		float doWhis = (mundo.PosicaoDesenhadaDe(ids.Whis) ?? Vector2.Inf).DistanceTo(mundo.PosicaoDesenhadaDe(eu) ?? Vector2.Zero);
		Afirmar("no meio do treino a dica e a do WHIS, esteja ele perto ou longe (a saida da luta ganha de tudo)", dica, $"dica '{E?.DicaNaTela}', o Whis a {doWhis:0} px");
		Tecla(Key.E);
		yield return 0.3;
		srv.ManterOHostDePeDeTeste(eu);
		Afirmar("no meio do treino o menu DELE vira a saida: um botao so, \"Parar o treino\"",
				E is { NaTela: true } && E.BotoesDesenhados().SequenceEqual(["Parar o treino"]),
				E != null ? $"dica={dica}, {E.TituloDesenhado}: {string.Join(" | ", E.BotoesDesenhados())}" : "");
		Fotografar("bills-08-o-menu-no-meio-do-treino");
		E?.ApertarDesenhado("Parar o treino");
		yield return 1.5;
		Afirmar("**PARAR** encerra o treino: o Whis volta ao posto, parado e sem luta -- e na tela ele esta de novo onde estava",
				srv.DivinoDeTeste(whis: true) is { Modo: "Parado", Parceiro: 0, Disciplina: false }
				&& (mundo.PosicaoDesenhadaDe(ids.Whis) ?? Vector2.Inf).DistanceTo(postoDoWhis) < 2f,
				$"{(mundo.PosicaoDesenhadaDe(ids.Whis) ?? Vector2.Inf).DistanceTo(postoDoWhis):0.0} px do posto");

		// =====================================================================
		// 5) O DESAFIO: A FURY NA TELA
		// =====================================================================
		Nota("-- 5) o desafio pelo titulo: o Bills na Fury --");
		srv.ManterOHostDePeDeTeste(eu);
		srv.DespertarOHostDeTeste(eu);
		srv.AoLadoDoDivinoDeTeste(eu, whis: false);
		for (int i = 0; i < 16 && E?.DicaNaTela != "[E] Bills"; i++) yield return 0.25;
		Tecla(Key.E);
		yield return 0.4;
		_falas.Clear();
		apertou = E?.ApertarDesenhado("Desafiar pelo título") == true;
		yield return 2.2;
		var duelo = srv.DivinoDeTeste(whis: false);
		Afirmar("**O DESAFIO ABRE PELO BOTAO**: no servidor o Bills esta no duelo com o host, na forma Fury, com o kit do Deus da Destruicao",
				apertou && duelo is { Desafio: true, Consagrado: true } d1 && d1.Parceiro == eu && d1.Forma == BillsEWhis.IdDaFury,
				duelo is { } d2 ? $"forma '{d2.Forma}', desafio={d2.Desafio}, parceiro {d2.Parceiro}" : "o Bills sumiu");
		vb = VisualDe(ids.Bills);
		Afirmar("**NA TELA O BILLS ESTA NA FURY**: a aura da forma esta no corpo dele (a mesma regua que deu zero no Whis)",
				vb != null && TemEfeitoDeForma(vb),
				vb == null ? "sem corpo" : $"corpo de forma={vb.CorpoDaFormaVisivelDeTeste}, coladas={vb.ColadasDeTeste}, contorno={vb.AuraDaFormaDeTeste:0.00}");
		Afirmar("o desafio e ANUNCIADO, e os dois falam antes da luta", Disse("DUELO PELO TÍTULO") && Disse(BillsEWhis.FalasDoDesafio[0].Texto), Ultimas());
		Fotografar("bills-09-o-desafio-o-bills-na-fury");

		Tecla(Key.E);
		yield return 0.3;
		Afirmar("no desafio o menu dele tambem vira a saida: \"Desistir do desafio\"",
				E is { NaTela: true } && E.BotoesDesenhados().SequenceEqual(["Desistir do desafio"]),
				E != null ? $"{E.TituloDesenhado}: {string.Join(" | ", E.BotoesDesenhados())}" : "");
		E?.ApertarDesenhado("Desistir do desafio");
		yield return 1.5;
		vb = VisualDe(ids.Bills);
		Afirmar("**DESISTIR** encerra: o Bills sai da Fury (no servidor e na tela), larga o kit e volta a ser objeto",
				srv.DivinoDeTeste(whis: false) is { Modo: "Parado", Parceiro: 0, Consagrado: false } fim && fim.Forma != BillsEWhis.IdDaFury
				&& vb != null && !TemEfeitoDeForma(vb),
				srv.DivinoDeTeste(whis: false) is { } f2 ? $"modo {f2.Modo}, forma '{f2.Forma}'" : "o Bills sumiu");
	}
}
