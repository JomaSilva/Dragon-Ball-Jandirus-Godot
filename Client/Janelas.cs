using Godot;

namespace Jandirus.Client;

/// <summary>
/// AS REGRAS DO ARRANJO DA TELA -- os numeros e as contas que prendem cada peca redimensionavel (o menu P, o
/// chat, os dois paineis do topo) na tela, no minimo e fora do caminho das vizinhas. Os dados moram no
/// <see cref="ArranjoDaInterface"/>; aqui mora o que se FAZ com eles.
///
/// ============================ UMA TRAVA SO, E ELA RODA SEMPRE ============================
/// O arquivo de preferencias pode trazer qualquer coisa (outra maquina, outro monitor, editado a mao), e a tela
/// muda de tamanho por baixo do arranjo (o jogador troca a resolucao). Por isso a trava nao e "ao carregar": cada
/// peca pergunta o seu lugar a uma funcao daqui toda vez que se aplica -- no nascimento, quando o arranjo muda
/// e quando a tela muda. Sao funcoes PURAS (recebem a tela e o arranjo, devolvem o retangulo): a bancada as
/// chama com telas e arquivos que nao existem.
///
/// O QUE CADA PECA RESPEITA:
///   * o MENU P embutido nunca sai da tela nem fica menor que <see cref="MenuMinimo"/>;
///   * o CHAT fica no canto dele (so o tamanho muda), nunca menor que <see cref="ChatMinimo"/>, nunca tao largo que
///     entre no canto inferior direito (o disco do radar, o convite do torneio) e nunca tao alto que encoste no
///     painel de status;
///   * os PAINEIS DO TOPO vao de <see cref="EscalaMinima"/> a <see cref="EscalaMaxima"/>, e o de status nao desce ate
///     o chat.
/// Quem mora EM VOLTA dessas pecas (a faixa da mineracao, a faixa do radio, a janela de desejos, o canto de
/// assistir o torneio, a dica "[E]") nao tem numero daqui: le o retangulo REAL da vizinha na tela.
/// ========================================================================================
/// </summary>
public static class Janelas
{
	// =====================================================================
	// OS NUMEROS (todos ajustaveis; em px da BASE DE DESENHO, a "resolucao" das opcoes)
	// =====================================================================
	/// <summary>O menu P de fabrica: 760 x 580, centrado. E o que as ancoras do `MenuJogo.Montar` sempre deram.</summary>
	public static readonly Vector2 MenuDeFabrica = new(760f, 580f);

	/// <summary>O menor menu P embutido: cabem o cabecalho com a busca, a barra de abas (que rola) e umas seis linhas da aba.</summary>
	public static readonly Vector2 MenuMinimo = new(520f, 320f);

	/// <summary>
	/// O menor CONTEUDO da janela destacada (antes da escala dela). Maior que o do embutido de proposito: a ficha
	/// de skill e o teclado do radio abrem POR CIMA do menu e, na janela, nao tem a tela do jogo pra onde sobrar.
	/// </summary>
	public static readonly Vector2 JanelaMinima = new(560f, 460f);

	/// <summary>A margem do chat ate as bordas esquerda e de baixo da tela. A de sempre (`Chat.Montar`).</summary>
	public const float ChatMargem = 12f;

	/// <summary>O chat de fabrica: 540 x 280.</summary>
	public static readonly Vector2 ChatDeFabrica = new(540f, 280f);

	/// <summary>
	/// O menor chat: os cinco filtros numa linha e seis linhas de fala -- quatro com a caixa de escrever aberta
	/// (ela toma 40 px do texto).
	/// </summary>
	public static readonly Vector2 ChatMinimo = new(300f, 180f);

	/// <summary>Ate que fracao da LARGURA da tela o chat pode ir.</summary>
	public const float ChatLarguraMaximaNaTela = 0.58f;

	/// <summary>
	/// O QUE O CANTO INFERIOR DIREITO RESERVA, em px: o disco do Dragon Radar (170 + 14 de margem + 8) e o convite
	/// do torneio encostado nele (300 + 14), mais um respiro. O chat nunca chega ali, por mais largo que fique.
	/// </summary>
	public const float ReservaDoCantoDireito = 530f;

	/// <summary>Sem painel de status na tela (bancada sem HUD), ate que fracao da ALTURA o chat pode ir.</summary>
	public const float ChatAlturaMaximaSemHud = 0.6f;

	/// <summary>A faixa de escala dos dois paineis do topo: de 70% a 160%.</summary>
	public const float EscalaMinima = 0.7f, EscalaMaxima = 1.6f;

	/// <summary>A margem dos paineis do topo ate a borda da tela. A de sempre (`Hud.MontarEsquerda`/`MontarDireita`).</summary>
	public const float MargemDoHud = 14f;

	/// <summary>O respiro entre duas pecas que nao podem se tocar (o chat e o painel de status, uma faixa e a vizinha).</summary>
	public const float FolgaEntrePecas = 8f;

	/// <summary>Ate que fracao da LARGURA da tela um painel do topo pode tomar (os dois, lado a lado, cabem).</summary>
	public const float PainelDoTopoMaximoNaTela = 0.5f;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--diagjanelas`); falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): nada e preso -- nem na tela, nem no minimo, nem na faixa de escala.
	/// O que o `config.json` disser e o que a peca faz. Lido pelas proprias linhas das travas, logo abaixo.
	/// </summary>
	public static bool SemTravaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): o monitor gravado da janela destacada nao e conferido -- ela e
	/// mandada pra um monitor que nao existe mais. Lido pela propria linha do <see cref="LugarDaJanela"/>.
	/// </summary>
	public static bool SemConferirOMonitorDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --diagjanelas): o arranjo mudado nao vai pro `config.json` -- morre com a sessao.</summary>
	public static bool NaoGravaDeTeste;

	// =====================================================================
	// O ARRANJO VIVO
	// =====================================================================
	/// <summary>O arranjo desta maquina. Sempre o do `Boot.Config` de AGORA (o "Restaurar" troca o objeto inteiro).</summary>
	public static ArranjoDaInterface Arranjo => Boot.Config.Arranjo;

	/// <summary>
	/// O ARRANJO MUDOU: cada peca se reaplica. **E ESTATICO** -- vive o processo inteiro, e uma assinatura que
	/// nao e cancelada aqui segura a tela morta pra sempre (a licao do `Verbos.Mudou`): metodo nomeado e `-=` no
	/// `_ExitTree` de quem assina.
	/// </summary>
	public static event Action? Mudou;

	/// <summary>Avisa as pecas de que o arranjo mudou. Nao grava: arrastar avisa a cada movimento e grava ao soltar.</summary>
	public static void Avisar() => Mudou?.Invoke();

	/// <summary>Poe o arranjo de agora no `config.json`.</summary>
	public static void Gravar()
	{
		if (NaoGravaDeTeste) return;
		Boot.Config.Gravar();
	}

	/// <summary>
	/// "RESTAURAR A INTERFACE PADRAO": tudo de volta ao lugar de fabrica -- o menu encaixado e centrado, o chat de
	/// 540 x 280, os dois paineis a 100%, as bordas soltas. Um objeto novo, e nao campo por campo: a peca que
	/// nascer depois desta lista ja nasce coberta.
	/// </summary>
	public static void Restaurar()
	{
		Boot.Config.Arranjo = new ArranjoDaInterface();
		Avisar();
		Gravar();
	}

	/// <summary>
	/// A JANELA DE BANCADA NAO PODE VIRAR A DONA DO TECLADO (`--semfoco`, ver `Boot._Ready`): a janela destacada
	/// obedece a mesma bandeira -- nasce sem foco e nunca o pede.
	/// </summary>
	public static readonly bool SemFoco = Array.IndexOf(OS.GetCmdlineArgs(), "--semfoco") >= 0;

	/// <summary>
	/// DA PRA ABRIR UMA JANELA DO SISTEMA? No `--headless` (e em plataforma de janela unica) nao: o botao
	/// "Destacar" nem aparece, e um arranjo gravado como destacado abre embutido.
	/// </summary>
	public static bool PodeDestacar => DisplayServer.HasFeature(DisplayServer.Feature.Subwindows);

	// =====================================================================
	// O MENU P EMBUTIDO
	// =====================================================================
	/// <summary>Onde o menu P embutido fica, em px da tela. De fabrica: centrado, 760 x 580.</summary>
	public static Rect2 MenuNaTela(ArranjoDaInterface a, Vector2 tela)
	{
		if (!a.MenuMexido) return new Rect2(((tela - MenuDeFabrica) / 2f).Round(), MenuDeFabrica);
		return PrenderNaTela(new Rect2(a.MenuX * tela.X, a.MenuY * tela.Y, a.MenuLargura * tela.X, a.MenuAltura * tela.Y),
							 tela, MenuMinimo);
	}

	/// <summary>
	/// PRENDE UM RETANGULO NA TELA: o tamanho entre o minimo e a tela inteira, e o canto de um jeito que nada fique
	/// de fora. O TAMANHO PRIMEIRO -- prender o canto antes deixaria de fora o que o tamanho corrigido acrescenta.
	/// </summary>
	public static Rect2 PrenderNaTela(Rect2 r, Vector2 tela, Vector2 minimo)
	{
		if (SemTravaDeTeste) return r;
		float l = Mathf.Round(Mathf.Clamp(r.Size.X, Mathf.Min(minimo.X, tela.X), tela.X));
		float h = Mathf.Round(Mathf.Clamp(r.Size.Y, Mathf.Min(minimo.Y, tela.Y), tela.Y));
		float x = Mathf.Round(Mathf.Clamp(r.Position.X, 0f, tela.X - l));
		float y = Mathf.Round(Mathf.Clamp(r.Position.Y, 0f, tela.Y - h));
		return new Rect2(x, y, l, h);
	}

	/// <summary>Guarda o retangulo do menu embutido como fracao da tela -- e a partir daqui ele deixa de ser o de fabrica.</summary>
	public static void GuardarMenu(ArranjoDaInterface a, Rect2 r, Vector2 tela)
	{
		if (tela.X <= 0f || tela.Y <= 0f) return;
		a.MenuMexido = true;
		a.MenuX = r.Position.X / tela.X;
		a.MenuY = r.Position.Y / tela.Y;
		a.MenuLargura = r.Size.X / tela.X;
		a.MenuAltura = r.Size.Y / tela.Y;
	}

	/// <summary>
	/// O RETANGULO DE UM ARRASTO: o de quando a borda foi pega, mais o quanto o mouse andou, do jeito que cada
	/// borda manda -- a borda oposta a que se puxa NAO se mexe (puxar a esquerda ate o minimo nao empurra o
	/// painel pra direita), e mover nao muda o tamanho.
	/// </summary>
	public static Rect2 Arrastado(Rect2 aoPegar, AlcaDeJanela.Lado lados, Vector2 andou, Vector2 tela, Vector2 minimo)
	{
		if (lados == AlcaDeJanela.Lado.Mover)
			return PrenderNaTela(new Rect2(aoPegar.Position + andou, aoPegar.Size), tela, minimo);

		float esq = aoPegar.Position.X, dir = aoPegar.End.X, cima = aoPegar.Position.Y, baixo = aoPegar.End.Y;
		if (SemTravaDeTeste)
		{
			if ((lados & AlcaDeJanela.Lado.Esquerda) != 0) esq += andou.X;
			if ((lados & AlcaDeJanela.Lado.Direita) != 0) dir += andou.X;
			if ((lados & AlcaDeJanela.Lado.Cima) != 0) cima += andou.Y;
			if ((lados & AlcaDeJanela.Lado.Baixo) != 0) baixo += andou.Y;
			return new Rect2(esq, cima, dir - esq, baixo - cima);
		}

		float minL = Mathf.Min(minimo.X, tela.X), minH = Mathf.Min(minimo.Y, tela.Y);
		if ((lados & AlcaDeJanela.Lado.Esquerda) != 0) esq = Mathf.Clamp(esq + andou.X, 0f, dir - minL);
		if ((lados & AlcaDeJanela.Lado.Direita) != 0) dir = Mathf.Clamp(dir + andou.X, esq + minL, tela.X);
		if ((lados & AlcaDeJanela.Lado.Cima) != 0) cima = Mathf.Clamp(cima + andou.Y, 0f, baixo - minH);
		if ((lados & AlcaDeJanela.Lado.Baixo) != 0) baixo = Mathf.Clamp(baixo + andou.Y, cima + minH, tela.Y);
		return new Rect2(Mathf.Round(esq), Mathf.Round(cima), Mathf.Round(dir - esq), Mathf.Round(baixo - cima));
	}

	// =====================================================================
	// O CHAT
	// =====================================================================
	/// <summary>
	/// O tamanho do chat, em px. `peDoStatus` e o Y do pe do painel de status NA TELA (0 = nao ha painel): o chat
	/// nao sobe ate ele.
	/// </summary>
	public static Vector2 ChatNaTela(ArranjoDaInterface a, Vector2 tela, float peDoStatus)
	{
		if (!a.ChatMexido) return ChatDeFabrica;
		return PrenderOChat(new Vector2(a.ChatLargura * tela.X, a.ChatAltura * tela.Y), tela, peDoStatus);
	}

	/// <summary>Prende um tamanho de chat entre o minimo e o teto desta tela.</summary>
	public static Vector2 PrenderOChat(Vector2 pedido, Vector2 tela, float peDoStatus)
	{
		if (SemTravaDeTeste) return pedido;
		Vector2 teto = TetoDoChat(tela, peDoStatus);
		return new Vector2(Mathf.Round(Mathf.Clamp(pedido.X, ChatMinimo.X, teto.X)),
						   Mathf.Round(Mathf.Clamp(pedido.Y, ChatMinimo.Y, teto.Y)));
	}

	/// <summary>
	/// O MAIOR CHAT QUE ESTA TELA ACEITA. Na largura: a fracao da tela, e nunca entrando na reserva do canto
	/// inferior direito. Na altura: ate um respiro abaixo do painel de status (o tamanho REAL dele, ja com a
	/// escala). Nunca menor que o minimo -- numa tela minuscula o minimo ganha, e as pecas se tocam.
	/// </summary>
	public static Vector2 TetoDoChat(Vector2 tela, float peDoStatus)
	{
		float l = Math.Min(tela.X * ChatLarguraMaximaNaTela, tela.X - ChatMargem - ReservaDoCantoDireito);
		float h = peDoStatus > 0f
			? tela.Y - ChatMargem - peDoStatus - FolgaEntrePecas
			: tela.Y * ChatAlturaMaximaSemHud;
		return new Vector2(Math.Max(l, ChatMinimo.X), Math.Max(h, ChatMinimo.Y));
	}

	/// <summary>Guarda o tamanho do chat como fracao da tela -- e a partir daqui ele deixa de ser o de fabrica.</summary>
	public static void GuardarChat(ArranjoDaInterface a, Vector2 tamanho, Vector2 tela)
	{
		if (tela.X <= 0f || tela.Y <= 0f) return;
		a.ChatMexido = true;
		a.ChatLargura = tamanho.X / tela.X;
		a.ChatAltura = tamanho.Y / tela.Y;
	}

	// =====================================================================
	// OS PAINEIS DO TOPO
	// =====================================================================
	/// <summary>
	/// A ESCALA QUE UM PAINEL DO TOPO PODE TER: a pedida, dentro da faixa, sem tomar mais que a metade da largura
	/// da tela e sem o pe dele passar do `chao` (o Y ate onde ele pode descer; 0 = sem vizinho embaixo, e ai o
	/// limite e o pe da tela, com a margem). `tamanhoBase` e o tamanho do painel a 100%.
	///
	/// O ESPACO SO TIRA O AUMENTO: a falta de lugar traz um painel aumentado de volta ate os 100%, e nunca abaixo
	/// disso -- numa tela pequena o painel de fabrica continua do tamanho de sempre (e encosta no vizinho, como
	/// sempre encostou). Menor que 100% so quem pediu.
	/// </summary>
	public static float EscalaQueCabe(float pedida, Vector2 tamanhoBase, Vector2 tela, float chao)
	{
		if (!float.IsFinite(pedida)) pedida = 1f;
		if (SemTravaDeTeste) return pedida;

		float s = Mathf.Clamp(pedida, EscalaMinima, EscalaMaxima);
		if (s > 1f && tamanhoBase.X > 1f && tamanhoBase.Y > 1f)
		{
			float pe = chao > 0f ? chao : tela.Y - MargemDoHud;
			s = Math.Min(s, (pe - MargemDoHud) / tamanhoBase.Y);
			s = Math.Min(s, (tela.X * PainelDoTopoMaximoNaTela - MargemDoHud) / tamanhoBase.X);
			s = Math.Max(s, 1f);
		}
		return s;
	}

	// =====================================================================
	// A JANELA DESTACADA
	// =====================================================================
	/// <summary>Um monitor: o retangulo inteiro dele e a parte que a barra de tarefas nao cobre, em px do desktop.</summary>
	public readonly record struct Monitor(Rect2I Inteiro, Rect2I Util);

	/// <summary>Os monitores desta maquina, AGORA (o jogador pluga e despluga monitor com o jogo aberto).</summary>
	public static List<Monitor> Monitores()
	{
		var lista = new List<Monitor>();
		int n = DisplayServer.GetScreenCount();
		for (int i = 0; i < n; i++)
		{
			var inteiro = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
			Rect2I util = DisplayServer.ScreenGetUsableRect(i);
			lista.Add(new Monitor(inteiro, util.Size.X > 0 && util.Size.Y > 0 ? util : inteiro));
		}
		return lista;
	}

	/// <summary>
	/// QUANTO A MOLDURA DE UMA JANELA COME, em px (largura, altura): a medida do `Settings` (tirada da janela do
	/// jogo vestida) ou, enquanto nunca houve uma (tela cheia desde o boot), a do Windows de fabrica.
	/// </summary>
	public static Vector2I MolduraDeJanela()
	{
		Vector2I m = Settings.Moldura;
		return m.X > 0 && m.Y > 0 ? m : new Vector2I(16, 39);
	}

	/// <summary>
	/// **ONDE A JANELA DESTACADA NASCE**: em que monitor, e o retangulo da area de desenho dela em px do desktop.
	///
	///   * COM LUGAR GRAVADO e o monitor ainda existe: no mesmo canto daquele monitor, com o mesmo tamanho;
	///   * COM LUGAR GRAVADO e o monitor NAO existe mais (o segundo monitor foi desplugado): no meio do monitor
	///     PRINCIPAL, com o mesmo tamanho -- nunca num canto de desktop que nenhuma tela mostra;
	///   * SEM LUGAR GRAVADO (o primeiro "Destacar"): em cima de onde o menu embutido estava (`sobreOPainel`).
	///
	/// Em todos os casos ela e PRESA na area util do monitor de destino, com a moldura contada: a barra de titulo
	/// fica na tela (senao nao ha como arrastar nem fechar), e a janela nao passa do tamanho dele.
	/// </summary>
	public static (int Tela, Rect2I Lugar) LugarDaJanela(ArranjoDaInterface a, IReadOnlyList<Monitor> monitores, int principal,
														 Rect2I sobreOPainel, Vector2I moldura, Vector2I minimo)
	{
		if (monitores.Count == 0) return (0, sobreOPainel);
		principal = Math.Clamp(principal, 0, monitores.Count - 1);

		if (!a.JanelaLembrada)
		{
			int onde = principal;
			Vector2I meio = sobreOPainel.Position + sobreOPainel.Size / 2;
			for (int i = 0; i < monitores.Count; i++)
				if (monitores[i].Inteiro.HasPoint(meio)) { onde = i; break; }
			return (onde, PrenderNoMonitor(sobreOPainel, monitores[onde].Util, moldura, minimo));
		}

		var tamanho = new Vector2I(a.JanelaLargura, a.JanelaAltura);
		bool existe = a.JanelaTela >= 0 && a.JanelaTela < monitores.Count;
		if (!existe && SemConferirOMonitorDeTeste)
			return (a.JanelaTela, new Rect2I(new Vector2I(a.JanelaX, a.JanelaY), tamanho));
		if (!existe)
		{
			Rect2I util = monitores[principal].Util;
			return (principal, PrenderNoMonitor(new Rect2I(util.Position + (util.Size - tamanho) / 2, tamanho), util, moldura, minimo));
		}

		Monitor m = monitores[a.JanelaTela];
		return (a.JanelaTela, PrenderNoMonitor(new Rect2I(m.Inteiro.Position + new Vector2I(a.JanelaX, a.JanelaY), tamanho),
											  m.Util, moldura, minimo));
	}

	/// <summary>
	/// Prende a AREA DE DESENHO de uma janela dentro da area util de um monitor, com a moldura em volta contada.
	/// A moldura do Windows nao e simetrica: `moldura.X / 2` de cada lado e embaixo, e o resto da altura em cima
	/// (a barra de titulo) -- a mesma conta do `Settings.Recentrar`.
	/// </summary>
	public static Rect2I PrenderNoMonitor(Rect2I r, Rect2I util, Vector2I moldura, Vector2I minimo)
	{
		if (SemTravaDeTeste) return r;
		int borda = moldura.X / 2, titulo = moldura.Y - borda;
		var teto = new Vector2I(Math.Max(1, util.Size.X - moldura.X), Math.Max(1, util.Size.Y - moldura.Y));
		int l = Math.Clamp(r.Size.X, Math.Min(minimo.X, teto.X), teto.X);
		int h = Math.Clamp(r.Size.Y, Math.Min(minimo.Y, teto.Y), teto.Y);
		int x = Math.Clamp(r.Position.X, util.Position.X + borda, util.End.X - borda - l);
		int y = Math.Clamp(r.Position.Y, util.Position.Y + titulo, util.End.Y - borda - h);
		return new Rect2I(x, y, l, h);
	}

	// =====================================================================
	// AS FAIXAS QUE MORAM JUNTO DO CHAT
	// =====================================================================
	/// <summary>
	/// **ONDE CABE UMA FAIXA "JUNTO DO CHAT"** (a da mineracao, o canto de assistir o torneio), dado o retangulo
	/// REAL do chat. Devolve o canto inferior esquerdo dela, em px da tela.
	///
	///   * EM CIMA do chat, alinhada com ele pela esquerda, um respiro acima -- o lugar de sempre. `jaEmpilhado`
	///     e a altura que outra faixa ja tomou ali (a de assistir sobe pra cima da da mineracao);
	///   * se ali ela bateria no painel de status (um chat alto com um painel grande nao deixam vao), vai pra
	///     DIREITA do chat, com o pe no pe dele.
	/// </summary>
	public static Vector2 JuntoDoChat(Rect2 chat, Vector2 faixa, float jaEmpilhado, float peDoStatus, float folga = FolgaEntrePecas)
	{
		float pe = chat.Position.Y - folga - jaEmpilhado;
		bool cabeEmCima = peDoStatus <= 0f || pe - faixa.Y >= peDoStatus + FolgaEntrePecas;
		return cabeEmCima
			? new Vector2(chat.Position.X, pe)
			: new Vector2(chat.End.X + FolgaEntrePecas, chat.End.Y - jaEmpilhado);
	}
}
