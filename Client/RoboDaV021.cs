using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.Verbos;
using Tarefa = System.Threading.Tasks.Task;
using Facing = Jandirus.Core.World.Facing;
using Fim = Jandirus.Core.Combat.FimDeProjetil;
using Vec2 = Jandirus.Core.World.Vec2;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DA VERSAO 0.2.1 (`--diagv021`) -- o pedido do dono de 2026-10-13, do lado de ca do fio.
///
/// A irma de servidor (`--v021teste`) prova as regras. Esta prova o que so existe na TELA e PELO FIO, com o defeito
/// do jogo de antes injetado em cada familia que tem um:
///
///   Z  A RODA DO MOUSE muda o zoom sem abrir a pausa, dentro dos limites do controle de la (item 21);
///   L  LARGAR pela mochila poe a coisa no chao (a lista da zona chega ao cliente) e a TECLA E a pega de volta, com a
///      dica na tela (item 5);
///   P  O PEDIDO vira um CARTAO com os dois botoes e o prazo, e responder pelo cartao o tira da tela e do servidor
///      (item 15);
///   W  OS PESOS na mochila se melhoram nas DUAS moedas (item 25), e o SCOUTER ligado e os PESOS vestidos aparecem no
///      boneco de quem os usa (itens 2 e 24);
///   B  A BANCADA DE CRIACAO (lote T7 da tech, item 13): o menu da tecla E abre a VITRINE -- a grade so com as pecas
///      que tem preco --, a peca escolhida chega tingida na mochila, o PENTE troca o penteado do boneco e a TINTA
///      troca a cor dos olhos;
///   R  REVISTAR um nocauteado mostra a CARTEIRA dele, e os dois botoes a levam (item 18);
///   G  O AGARRAO (item 36): com alguem na mao o golpe APERTA O PESCOCO sem a pose de soco e planta o corpo; no colo,
///      o carregado e desenhado NO PONTO de quem o carrega; e o agarrado que aperta uma direcao so VIRA;
///   F  AS FORMAS (itens 19 e 20): o penteado sem a arte de Super Saiyajin ganha o DOURADO, o Namekuseijin de
///      potencial desperto AMARELA, e o ORANGE pinta o corpo de laranja e o deixa maior;
///   K  O KI (itens 1, 7, 8, 11 e 34): a bola nasce com o som do original e a pose de tiro, sem plantar quem atira
///      andando; no fim do alcance ela ESTOURA (o estalo e a cratera pequena); a carga do raio se ouve, o som do
///      raio anda com ele, e ele EXPLODE no fim (o estrondo e a cratera grande); e a tecla X tem o som dela.
///
/// O QUE ELA NAO PROVA: pixel nenhum. Sem janela o que se mede e a INTENCAO -- a tinta e o modo escritos no material,
/// a escala da camada, o arquivo de som que de fato foi tocado, o decalque que chegou pelo fio --, e nao a cor que
/// saiu na tela. As naves (item 26), o Majin sem sangue (item 16) e os menus das maquinas novas (item 13) continuam
/// medidos so do lado do servidor, pela `--v021teste`.
///
///     Godot --headless --path . --host --rede 8283 --campoteste 8 --horateste 0.5 --diagv021 --raca Human --conta bancada_dvum --nome Vinteum
/// </summary>
public partial class RoboDaV021 : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dv021] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>O boneco do corpo local, como a tela o desenha.</summary>
	private static CharacterVisual? Boneco() =>
		C is { } cli ? World.Instancia?.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull<CharacterVisual>("Visual") : null;

	/// <summary>O corpo local -- quem le o teclado e diz por que nao anda.</summary>
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	public override void _Ready()
	{
		ProcessPriority = -100;
		_ = Rodar();
	}

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum botao de defeito sobrevive ao roteiro.
		World.ScrollNaoMudaOZoomDeTeste = false;
		World.RodaSobreMenuMexeNoZoomDeTeste = false;
		MenuDeInteracao.PegarDoChaoSoPeloVerboDeTeste = false;
		Chao.LargarApagaDeTeste = false;
		PedidosPendentes.SemNotificacaoDeTeste = false;
		TelaDeInventario.MelhoriaSoComZeniDeTeste = false;
		EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = false;
		Saque.CarteiraFicaDeTeste = false;
		LocalPlayer.EstranguladorAndaDeTeste = false;
		LocalPlayer.EstrangularTocaOSocoDeTeste = false;
		LocalPlayer.AgarradoNaoViraDeTeste = false;
		RemotePlayer.AgarradoVemAtrasadoDeTeste = false;
		Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = false;
		Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = false;
		Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = false;
		Jandirus.Core.Combat.KiContraCorpo.BolaSomeAoAcabarDeTeste = false;
		Jandirus.Core.Combat.SonsDeKi.TiroSemSomDeTeste = false;
		Jandirus.Server.GameServer.GestoPrendeNaTelaDeTeste = false;
		CargaDeRaioVisual.CargaMudaDeTeste = false;
		if (S is { } servidor) servidor.XSemSomDeTeste = false;

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" A VERSAO 0.2.1 NA TELA -- o pedido de 2026-10-13");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && c.LocalId != 0
									  && S != null && World.Instancia != null && TelaDeInventario.Instancia != null
									  && TelaDeArmadura.Instancia != null && MenuDeInteracao.Instancia != null && Boneco() != null && Corpo != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, mochila, grade de arte, menu da tecla E, boneco, corpo) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || World.Instancia is not { } mundo
			|| TelaDeInventario.Instancia is not { } mochila || TelaDeArmadura.Instancia is not { } grade
			|| MenuDeInteracao.Instancia is not { } menuE || Corpo is not { } corpo) { Encerrar(2); return; }
		await Segundos(1.0);

		await Z_OZoomPelaRoda(mundo);
		await L_LargarEPegar(cli, srv, mochila, menuE);
		await P_OPedidoNaTela(cli, srv);
		await W_OsPesosEOScouter(cli, srv, mochila);
		await B_ABancadaDeCriacao(cli, srv, mochila, grade, menuE);
		await R_ACarteiraDoCaido(cli, srv, mochila, mundo);
		await G_OAgarraoNaTela(cli, srv, mundo, corpo);
		await F_AsFormasNaTela(cli, srv, mundo);
		// (O KI POR ULTIMO: os tiros voam pelo campo, e nenhuma outra familia precisa dele limpo depois.)
		await K_OKiNaTela(cli, srv, mundo, corpo);

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// Z) A RODA DO MOUSE MUDA O ZOOM (item 21)
	// =====================================================================
	private async Tarefa Z_OZoomPelaRoda(World mundo)
	{
		Nota("--- Z: a roda do mouse muda o zoom, sem abrir a pausa ---");
		Settings cfg = Boot.Config;
		int deAntes = cfg.Zoom;
		// A RODA ENTRA PELA PORTA QUE O MOTOR USA (`_UnhandledInput`), e nao pelo `Input.ParseInputEvent`: sem janela nao
		// ha "em cima do mundo" pra roda cair, e um painel da tela poderia consumi-la antes.
		void Roda(bool praCima) => mundo._UnhandledInput(new InputEventMouseButton
		{
			ButtonIndex = praCima ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true,
		});
		bool Em(int z) => cfg.Zoom == z && Mathf.IsEqualApprox(mundo.ZoomDaCameraDeTeste, z);
		try
		{
			cfg.Zoom = 3;
			mundo.AplicarZoom(3);
			await Quadros(2);
			Roda(praCima: true);
			await Quadros(2);
			bool aproximou = Em(4);
			Roda(praCima: false);
			Roda(praCima: false);
			await Quadros(2);
			bool afastou = Em(2);
			Roda(praCima: false);
			await Quadros(2);
			bool piso = Em(Settings.ZoomMin);
			for (int i = 0; i < 10; i++) Roda(praCima: true);
			await Quadros(2);
			bool teto = Em(Settings.ZoomMax);
			Checa("**A RODA DO MOUSE MUDA O ZOOM**: um dente pra cima aproxima um passo, pra baixo afasta, a camera acompanha e a preferencia fica gravada -- e a roda para no piso e no teto do controle da pausa",
				  aproximou && afastou && piso && teto && !PausaAberta(),
				  $"aproximou {aproximou}; afastou {afastou}; piso {piso}; teto {teto}; zoom {cfg.Zoom}, camera {mundo.ZoomDaCameraDeTeste:0.##}; pausa aberta {PausaAberta()}");

			cfg.Zoom = 3;
			mundo.AplicarZoom(3);
			await Quadros(2);
			World.ScrollNaoMudaOZoomDeTeste = true;
			try
			{
				Roda(praCima: true);
				await Quadros(2);
				Injeta("(a roda nao mexe no zoom) o dente pra cima deixa o zoom onde estava -- o jogo de antes, em que so a pausa o mudava", Em(3), $"zoom {cfg.Zoom}");
			}
			finally { World.ScrollNaoMudaOZoomDeTeste = false; }

			// ============================ A RODA EM CIMA DE UM MENU NAO MEXE NO ZOOM (dono, 2026-10-13) ============================
			// *"enquanto o mouse esta em cima de qualquer menu (inventario, menu P, pausa ou oq for) o zoom da camera n deveria
			// acontecer, somente se tiver com menu fechado ou o mouse fora da area do menu"*. AQUI A RODA ENTRA PELA PORTA DE
			// VERDADE (`Viewport.PushInput`, com a posicao): a pergunta e justamente ONDE ela caiu, e quem responde e o motor
			// (o controle sob o mouse). Sempre o dente PRA CIMA: uma lista aberta esta no topo, e pra cima ela nao rola -- e a
			// roda que a lista nao usou e a que vazava.
			// =======================================================================================================================
			Viewport tela = GetViewport();
			Vector2 tamanho = tela.GetVisibleRect().Size;
			void Tres() { cfg.Zoom = 3; mundo.AplicarZoom(3); }
			string SobOMouse() => tela.GuiGetHoveredControl() is { } sob ? $"{sob.GetType().Name} {sob.GetPath()}" : "(nada)";
			bool RodaEm(Vector2 p)
			{
				Tres();
				tela.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
				tela.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = p, GlobalPosition = p }, true);
				bool mexeu = cfg.Zoom != 3;
				Tres();
				return mexeu;
			}
			(int Mexeram, string Onde) Varrer(Rect2 r)
			{
				int mexeram = 0;
				string onde = "";
				for (int i = 0; i < 5; i++)
					for (int j = 0; j < 5; j++)
					{
						var p = new Vector2(r.Position.X + 8 + (r.Size.X - 16) * i / 4f, r.Position.Y + 8 + (r.Size.Y - 16) * j / 4f);
						if (!RodaEm(p)) continue;
						mexeram++;
						if (onde.Length == 0) onde = $"(o primeiro: {p}, sobre {SobOMouse()})";
					}
				return (mexeram, onde);
			}
			int VazavamEm(Rect2 r)
			{
				World.RodaSobreMenuMexeNoZoomDeTeste = true;
				try { return Varrer(r).Mexeram; }
				finally { World.RodaSobreMenuMexeNoZoomDeTeste = false; }
			}

			// (um ponto do MUNDO: a meia altura, perto da borda direita -- fora da mochila e do menu P, que nascem no centro)
			var noMundo = new Vector2(tamanho.X * 0.88f, tamanho.Y * 0.5f);
			await Quadros(2);
			bool livre = RodaEm(noMundo);
			Checa("(pela porta de verdade) com os menus fechados, a roda em cima do mundo muda o zoom", livre, $"em {noMundo} de {tamanho}; sob o mouse: {SobOMouse()}");

			TelaDeInventario mochila = TelaDeInventario.Instancia!;
			mochila.Abrir();
			await Quadros(3);
			Rect2 rMochila = mochila.RetanguloDeTeste;
			(int naMochila, string ondeNaMochila) = Varrer(rMochila);
			bool foraDaMochila = !rMochila.HasPoint(noMundo) && RodaEm(noMundo);
			string sobForaDaMochila = SobOMouse();
			int vazavamNaMochila = VazavamEm(rMochila);
			mochila.Fechar();
			await Quadros(2);
			Checa("**A RODA EM CIMA DA MOCHILA NAO MEXE NO ZOOM**: vinte e cinco pontos do painel, nenhum muda o zoom -- e com ela aberta, o mouse FORA do painel continua mudando",
				  rMochila.Size.X > 100 && naMochila == 0 && foraDaMochila,
				  $"painel {rMochila}; mexeram {naMochila} {ondeNaMochila}; fora do painel mexeu {foraDaMochila} (sob o mouse: {sobForaDaMochila})");
			Injeta("(a roda atravessa o menu) em cima da mochila aberta a roda volta a mexer no zoom -- o painel nao tem lista que a segure", vazavamNaMochila > 0, $"{vazavamNaMochila} de 25 pontos");

			MenuJogo menuP = MenuJogo.Instancia!;
			menuP.Abrir();
			await Quadros(3);
			Rect2 rMenu = MenuJogo.RetanguloNaTela ?? default;
			(int noMenu, string ondeNoMenu) = Varrer(rMenu);
			bool foraDoMenu = !rMenu.HasPoint(noMundo) && RodaEm(noMundo);
			int vazavamNoMenu = VazavamEm(rMenu);
			menuP.Fechar();
			await Quadros(2);
			Checa("**...NEM EM CIMA DO MENU P**: vinte e cinco pontos da janela dele, nenhum muda o zoom -- e fora dela continua mudando",
				  rMenu.Size.X > 100 && noMenu == 0 && foraDoMenu, $"janela {rMenu}; mexeram {noMenu} {ondeNoMenu}; fora dela mexeu {foraDoMenu}");
			Injeta("(a roda atravessa o menu) em cima do menu P a roda volta a mexer no zoom onde a pagina nao rola", vazavamNoMenu > 0, $"{vazavamNoMenu} de 25 pontos");

			PauseMenu pausa = PauseMenu.Instancia!;
			pausa.Abrir();
			await Quadros(3);
			bool pausaNoMeio = RodaEm(tamanho / 2), pausaNoVeu = RodaEm(noMundo);
			string sobAPausa = SobOMouse();
			World.RodaSobreMenuMexeNoZoomDeTeste = true;
			bool vazavaNaPausa;
			try { vazavaNaPausa = RodaEm(noMundo); }
			finally { World.RodaSobreMenuMexeNoZoomDeTeste = false; }
			pausa.Fechar("a bancada fechou");
			await Quadros(2);
			Checa("**...NEM COM A PAUSA ABERTA**: o veu dela cobre a tela inteira, e a roda nao mexe no zoom nem no meio do painel nem na beirada",
				  !pausaNoMeio && !pausaNoVeu, $"no meio mexeu {pausaNoMeio}; no veu mexeu {pausaNoVeu} (sob o mouse: {sobAPausa})");
			Injeta("(a roda atravessa o menu) com a pausa aberta a roda em cima do veu volta a mexer no zoom", vazavaNaPausa);
			bool voltou = RodaEm(noMundo);
			Checa("...e fechados os tres, a roda em cima do mundo volta a mudar o zoom", voltou, $"sob o mouse: {SobOMouse()}");
		}
		finally
		{
			World.ScrollNaoMudaOZoomDeTeste = false;
			World.RodaSobreMenuMexeNoZoomDeTeste = false;
			cfg.Zoom = deAntes;
			cfg.Gravar();
			mundo.AplicarZoom(deAntes);
		}
		await Quadros(2);
	}

	private static bool PausaAberta() => PauseMenu.Instancia is { Aberto: true };

	// =====================================================================
	// L) LARGAR PELA MOCHILA, PEGAR PELA TECLA E (item 5)
	// =====================================================================
	private async Tarefa L_LargarEPegar(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila, MenuDeInteracao menuE)
	{
		Nota("--- L: largar pela mochila poe no chao, e a tecla E pega de volta ---");
		const string maca = CatalogoDeItens.Maca;
		const string largar = "Largar no chão";
		srv.PalcoV21_Limpar(cli.LocalId);
		srv.PalcoV21_Dar(cli.LocalId, maca, 1);
		bool chegou = await Ate(() => cli.Mochila.Quantos(maca) == 1 && cli.Largados.Count == 0, 4);
		Checa("(montagem) uma maca na mochila, e nada no chao da zona", chegou, $"macas {cli.Mochila.Quantos(maca)}, no chao {cli.Largados.Count}");
		if (!chegou) return;

		async System.Threading.Tasks.Task<bool> LargarPelaMochila()
		{
			mochila.Abrir();
			await Quadros(3);
			bool abriu = mochila.AbrirAcoesDeTeste(maca);
			await Quadros(2);
			bool apertou = abriu && mochila.ApertarAcaoDeTeste(largar);
			await Quadros(2);
			mochila.Fechar();
			return apertou;
		}

		try
		{
			mochila.Abrir();
			await Quadros(3);
			bool temOBotao = mochila.AbrirAcoesDeTeste(maca) && mochila.AcoesAbertasDeTeste().Exists(a => a.Texto == largar);
			mochila.Fechar();
			await Quadros(2);

			bool apertou = await LargarPelaMochila();
			bool caiu = await Ate(() => cli.Mochila.Quantos(maca) == 0 && cli.Largados.Exists(l => l.Item == maca), 4);
			Checa("**LARGAR POE NO CHAO**: a mochila tem o botao \"Largar no chão\", o item sai dela e a lista de coisas largadas da zona chega ao cliente com ele",
				  temOBotao && apertou && caiu && srv.PalcoV21_NoChao(cli.LocalId, maca).Deste == 1,
				  $"botao {temOBotao}; apertou {apertou}; macas na mochila {cli.Mochila.Quantos(maca)}; no chao (cliente) {cli.Largados.Count}; no chao (servidor) {srv.PalcoV21_NoChao(cli.LocalId, maca)}");

			bool dica = await Ate(() => menuE.DicaNaTela.Length > 0, 3);
			string aDica = menuE.DicaNaTela;

			MenuDeInteracao.PegarDoChaoSoPeloVerboDeTeste = true;
			try
			{
				await Segundos(0.4);
				string dicaComDefeito = menuE.DicaNaTela;
				Toque(Key.E);
				await Segundos(0.6);
				Injeta("(a tecla E nao enxerga o chao) a dica some e o E nao pega nada: a maca continua no chao, sem porta de volta",
					   dicaComDefeito.Length == 0 && cli.Mochila.Quantos(maca) == 0 && cli.Largados.Exists(l => l.Item == maca),
					   $"dica '{dicaComDefeito}'; macas {cli.Mochila.Quantos(maca)}");
				if (menuE.NaTela) Toque(Key.Escape);
			}
			finally { MenuDeInteracao.PegarDoChaoSoPeloVerboDeTeste = false; }
			await Quadros(3);

			Toque(Key.E);
			bool pegou = await Ate(() => cli.Mochila.Quantos(maca) == 1 && !cli.Largados.Exists(l => l.Item == maca), 4);
			Checa("**A TECLA E PEGA O QUE ESTA NO CHAO**: com a coisa ao alcance a dica aparece, e o E a devolve pra mochila sem abrir menu nenhum",
				  dica && pegou && !menuE.NaTela, $"dica '{aDica}'; macas {cli.Mochila.Quantos(maca)}; no chao {cli.Largados.Count}; menu aberto {menuE.NaTela}");

			Chao.LargarApagaDeTeste = true;
			try
			{
				bool apertouDeNovo = await LargarPelaMochila();
				bool sumiu = await Ate(() => cli.Mochila.Quantos(maca) == 0, 4);
				await Segundos(0.5);
				Injeta("(largar apaga o item) a maca sai da mochila e NAO aparece no chao -- o \"Jogar fora\" da v0.2.0",
					   apertouDeNovo && sumiu && !cli.Largados.Exists(l => l.Item == maca) && srv.PalcoV21_NoChao(cli.LocalId, maca).Deste == 0,
					   $"macas {cli.Mochila.Quantos(maca)}; no chao {cli.Largados.Count}");
			}
			finally { Chao.LargarApagaDeTeste = false; }
		}
		finally
		{
			MenuDeInteracao.PegarDoChaoSoPeloVerboDeTeste = false;
			Chao.LargarApagaDeTeste = false;
			mochila.Fechar();
			srv.PalcoV21_Limpar(cli.LocalId);
		}
		await Segundos(0.3);
	}

	// =====================================================================
	// P) O PEDIDO VIRA UM CARTAO NA TELA (item 15)
	// =====================================================================
	private async Tarefa P_OPedidoNaTela(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- P: o convite de mestre vira um cartao com Aceitar e Recusar ---");
		PainelDePedidos? painel = Todos(GetTree().Root).OfType<PainelDePedidos>().FirstOrDefault();
		Checa("(montagem) o painel de pedidos esta montado na tela", painel != null);
		if (painel == null) return;
		(string sim, string nao) = PedidosPendentes.Rotulos(Fatos.ConviteDeMestre);

		int mestre = srv.PalcoV21_Mestre(cli.LocalId);
		Checa("(montagem) um mestre de bancada, de frente pro jogador, o convidou pela porta de producao", mestre != 0);
		if (mestre == 0) return;
		try
		{
			bool chegou = await Ate(() => cli.Pedidos.Count == 1 && painel.CartoesDeTeste().Count == 1, 5);
			(string Texto, List<string> Botoes, double Segundos) cartao = painel.CartoesDeTeste().FirstOrDefault();
			Checa("**O PEDIDO VIRA UM CARTAO NA TELA**: o convite chega pelo fio e ganha um cartao com o texto, os dois botoes (os mesmos verbs do menu) e o prazo correndo",
				  chegou && cartao.Texto is { Length: > 0 } && cartao.Botoes is { Count: 2 } && cartao.Botoes.Contains(sim) && cartao.Botoes.Contains(nao)
				  && cartao.Segundos > 0 && cli.Pedidos[0].Fato == Fatos.ConviteDeMestre,
				  chegou ? $"'{cartao.Texto}' [{string.Join(" | ", cartao.Botoes ?? [])}] {cartao.Segundos:0} s" : $"pedidos {cli.Pedidos.Count}, cartoes {painel.CartoesDeTeste().Count}");
			if (!chegou) return;

			bool apertou = painel.ApertarDeTeste(cartao.Texto, nao);
			bool sumiu = await Ate(() => cli.Pedidos.Count == 0 && painel.CartoesDeTeste().Count == 0 && !srv.PalcoV21_TemConvite(cli.LocalId), 5);
			Checa("**RESPONDER PELO CARTAO**: \"Recusar\" manda o verb, o convite acaba no servidor e o cartao some da tela",
				  apertou && sumiu, $"apertou {apertou}; pedidos {cli.Pedidos.Count}; cartoes {painel.CartoesDeTeste().Count}; no servidor {srv.PalcoV21_TemConvite(cli.LocalId)}");

			PedidosPendentes.SemNotificacaoDeTeste = true;
			try
			{
				bool convidou = srv.PalcoV21_ConvidarDeNovo(mestre, cli.LocalId);
				await Segundos(1.5);
				Injeta("(nenhum pedido vira notificacao) o convite existe no servidor e a tela nao mostra cartao nenhum -- a linha de chat da v0.2.0",
					   convidou && srv.PalcoV21_TemConvite(cli.LocalId) && cli.Pedidos.Count == 0 && painel.CartoesDeTeste().Count == 0,
					   $"convidou {convidou}; no servidor {srv.PalcoV21_TemConvite(cli.LocalId)}; cartoes {painel.CartoesDeTeste().Count}");
			}
			finally { PedidosPendentes.SemNotificacaoDeTeste = false; }
		}
		finally
		{
			PedidosPendentes.SemNotificacaoDeTeste = false;
			srv.PalcoV21_SemConvite(cli.LocalId);
			srv.PalcoV21_TirarOFigurante(mestre);
		}
		await Segundos(0.5);
	}

	// =====================================================================
	// W) OS PESOS NAS DUAS MOEDAS; O SCOUTER E OS PESOS NO BONECO (itens 2, 24 e 25)
	// =====================================================================
	private async Tarefa W_OsPesosEOScouter(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila)
	{
		Nota("--- W: os pesos se melhoram nas duas moedas, e o que se usa aparece no boneco ---");
		const string pesos = CatalogoDeItens.Pesos, scouter = CatalogoDeItens.Scouter;
		srv.PalcoV21_Limpar(cli.LocalId);
		srv.PalcoV21_Dar(cli.LocalId, pesos, 1);
		srv.PalcoV21_Dar(cli.LocalId, scouter, 1);
		srv.PalcoV21_Carteira(cli.LocalId, 1_000_000, 1_000_000);
		bool chegou = await Ate(() => cli.Mochila.Quantos(pesos) == 1 && cli.Mochila.Quantos(scouter) == 1, 4);
		Checa("(montagem) os pesos e um scouter na mochila", chegou);
		if (!chegou) return;

		List<(string Texto, string Moeda)> Melhorias()
		{
			mochila.Abrir();
			bool abriu = mochila.AbrirAcoesDeTeste(pesos);
			List<(string Texto, string Moeda)> botoes = abriu ? mochila.AcoesAbertasDeTeste().FindAll(a => a.Texto.StartsWith("Melhorar:", StringComparison.Ordinal)) : [];
			mochila.Fechar();
			return botoes;
		}

		try
		{
			await Quadros(3);
			List<(string Texto, string Moeda)> duas = Melhorias();
			Checa("**OS PESOS SE MELHORAM NAS DUAS MOEDAS**: o painel de acoes deles tem um botao \"Melhorar\" em zeni e outro em recursos",
				  duas.Count == 2 && duas.Exists(b => b.Texto.Contains("zeni")) && duas.Exists(b => b.Texto.Contains("recursos")),
				  string.Join(" | ", duas.Select(b => b.Texto)));
			TelaDeInventario.MelhoriaSoComZeniDeTeste = true;
			try
			{
				await Quadros(2);
				List<(string Texto, string Moeda)> uma = Melhorias();
				Injeta("(a melhoria so em zeni) o painel dos pesos volta a ter um botao so -- a v0.2.0", uma.Count == 1 && uma[0].Texto.Contains("zeni"),
					   string.Join(" | ", uma.Select(b => b.Texto)));
			}
			finally { TelaDeInventario.MelhoriaSoComZeniDeTeste = false; }

			// ---- o scouter e os pesos no boneco ----
			(bool Pesos, bool Scouter) nu = Boneco()?.UsandoDeTeste ?? (true, true);
			cli.SendVerbo("item_equipar", scouter);
			cli.SendVerbo("item_ajustar", $"{pesos}/50");
			bool vestiu = await Ate(() => Boneco()?.UsandoDeTeste is { Pesos: true, Scouter: true }, 5);
			(bool Scouter, double Peso) noServidor = srv.PalcoV21_Usando(cli.LocalId);
			Checa("**O QUE SE USA APARECE NO BONECO**: sem nada o corpo nao tem camada de scouter nem de pesos; ligado o scouter e vestidos os pesos, as duas camadas aparecem por cima dele",
				  !nu.Pesos && !nu.Scouter && vestiu && noServidor.Scouter && noServidor.Peso > 0,
				  $"antes {nu}; depois {Boneco()?.UsandoDeTeste}; no servidor: scouter {noServidor.Scouter}, peso {noServidor.Peso:0.##}");

			cli.SendVerbo("item_equipar", scouter);
			cli.SendVerbo("item_tirar", pesos);
			bool tirou = await Ate(() => Boneco()?.UsandoDeTeste is { Pesos: false, Scouter: false }, 5);
			Checa("...e tirar os dois os tira do boneco", tirou, $"{Boneco()?.UsandoDeTeste}");

			EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = true;
			try
			{
				cli.SendVerbo("item_equipar", scouter);
				cli.SendVerbo("item_ajustar", $"{pesos}/50");
				bool ligouNoServidor = await Ate(() => srv.PalcoV21_Usando(cli.LocalId) is { Scouter: true, Peso: > 0 }, 5);
				await Segundos(0.8);
				Injeta("(o que se usa nao aparece no corpo) o scouter esta ligado e os pesos vestidos no servidor, e o boneco continua igual -- a v0.2.0",
					   ligouNoServidor && Boneco()?.UsandoDeTeste is { Pesos: false, Scouter: false }, $"no servidor {srv.PalcoV21_Usando(cli.LocalId)}; no boneco {Boneco()?.UsandoDeTeste}");
			}
			finally { EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = false; }
		}
		finally
		{
			TelaDeInventario.MelhoriaSoComZeniDeTeste = false;
			EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = false;
			if (srv.PalcoV21_Usando(cli.LocalId).Scouter) cli.SendVerbo("item_equipar", scouter);
			if (srv.PalcoV21_Usando(cli.LocalId).Peso > 0) cli.SendVerbo("item_tirar", pesos);
			await Segundos(0.6);
			mochila.Fechar();
			srv.PalcoV21_Limpar(cli.LocalId);
		}
		await Segundos(0.3);
	}

	// =====================================================================
	// R) A CARTEIRA DO CAIDO (item 18)
	// =====================================================================
	private async Tarefa R_ACarteiraDoCaido(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila, World mundo)
	{
		Nota("--- R: revistar um nocauteado mostra a carteira dele, e os dois botoes a levam ---");
		srv.PalcoV21_Carteira(cli.LocalId, 0, 0);
		int caido = srv.CaidoVestidoDeTeste(cli.LocalId);
		Checa("(montagem) um corpo de bancada nocauteado apareceu ao lado, com 500 zeni e 300 recursos na carteira",
			  caido != 0 && srv.PalcoV21_Carteira(caido, 500, 300));
		if (caido == 0) return;
		try
		{
			bool visto = await Ate(() => mundo.PosicaoDesenhadaDe(cli.LocalId) is { } eu
										 && mundo.CaidoPerto(eu, Interacoes.Alcance, cli.CadaverPerto) is { } c && c.Id == caido, 5);
			Checa("o cliente ve o caido ao alcance da tecla E", visto);

			Saque.CarteiraFicaDeTeste = true;
			try
			{
				Toque(Key.E);
				bool abriuComDefeito = await Ate(() => mochila.SaqueDeTeste.Aberto, 4);
				await Quadros(3);
				List<(string Texto, bool Ligado)> semCarteira = mochila.CarteiraDeTeste();
				Injeta("(a carteira do caido fica com ele) a tela de revistar abre e os dois botoes da carteira vem apagados -- o saque de antes",
					   abriuComDefeito && !semCarteira.Exists(b => b.Ligado && b.Texto.StartsWith("Levar", StringComparison.Ordinal)),
					   string.Join(" | ", semCarteira.Select(b => $"{b.Texto} ({(b.Ligado ? "aceso" : "apagado")})")));
				Toque(Key.Escape);
				await Ate(() => !mochila.SaqueDeTeste.Aberto, 2);
			}
			finally { Saque.CarteiraFicaDeTeste = false; }
			await Quadros(3);

			Toque(Key.E);
			bool abriu = await Ate(() => mochila.SaqueDeTeste.Aberto && mochila.CarteiraDeTeste().Exists(b => b.Ligado && b.Texto.StartsWith("Levar o zeni", StringComparison.Ordinal)), 4);
			List<(string Texto, bool Ligado)> carteira = mochila.CarteiraDeTeste();
			Checa("**REVISTAR MOSTRA A CARTEIRA DO CAIDO**: a tela da mochila abre na mochila dele, com \"Levar o zeni (500)\" e \"Levar os recursos (300)\" acesos",
				  abriu && carteira.Exists(b => b.Ligado && b.Texto == "Levar o zeni (500)") && carteira.Exists(b => b.Ligado && b.Texto == "Levar os recursos (300)"),
				  string.Join(" | ", carteira.Select(b => $"{b.Texto} ({(b.Ligado ? "aceso" : "apagado")})")));

			bool apertouOZeni = mochila.ApertarNaCarteiraDeTeste("Levar o zeni");
			bool levouOZeni = await Ate(() => Math.Abs(srv.PalcoV21_CarteiraDe(cli.LocalId).Zeni - 500) < 0.5 && srv.PalcoV21_CarteiraDe(caido).Zeni < 0.5, 4);
			// (o servidor da um respiro entre dois saques -- `Saque.SegundosEntreSaques` --, e a tela volta com o que sobrou)
			await Segundos(Saque.SegundosEntreSaques + 0.3);
			await Ate(() => mochila.SaqueDeTeste.Aberto && mochila.CarteiraDeTeste().Exists(b => !b.Ligado && b.Texto.StartsWith("Levar o zeni", StringComparison.Ordinal))
							&& mochila.CarteiraDeTeste().Exists(b => b.Ligado && b.Texto.StartsWith("Levar os recursos", StringComparison.Ordinal)), 3);
			bool apertouOsRecursos = mochila.ApertarNaCarteiraDeTeste("Levar os recursos");
			bool levouOsRecursos = await Ate(() => Math.Abs(srv.PalcoV21_CarteiraDe(cli.LocalId).Recursos - 300) < 0.5 && srv.PalcoV21_CarteiraDe(caido).Recursos < 0.5, 4);
			bool naMinhaTela = await Ate(() => Math.Abs(cli.Zeni - 500) < 0.5, 3);
			Checa("**OS DOIS BOTOES LEVAM A CARTEIRA INTEIRA**: o zeni e os recursos saem dele e chegam a quem revista -- e a carteira de quem revista chega atualizada ao cliente",
				  apertouOZeni && levouOZeni && apertouOsRecursos && levouOsRecursos && naMinhaTela,
				  $"zeni {apertouOZeni}/{levouOZeni}; recursos {apertouOsRecursos}/{levouOsRecursos}; meu {srv.PalcoV21_CarteiraDe(cli.LocalId)}, dele {srv.PalcoV21_CarteiraDe(caido)}; no cliente {cli.Zeni:0}");
		}
		finally
		{
			Saque.CarteiraFicaDeTeste = false;
			if (mochila.SaqueDeTeste.Aberto) Toque(Key.Escape);
			await Quadros(3);
			mochila.Fechar();
			srv.PalcoV21_TirarOFigurante(caido);
			srv.PalcoV21_Limpar(cli.LocalId);
		}
		await Segundos(0.5);
	}

	// =====================================================================
	// B) A BANCADA DE CRIACAO: a vitrine, o pente e a tinta de olhos (item 13, lote T7)
	// =====================================================================
	private async Tarefa B_ABancadaDeCriacao(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila, TelaDeArmadura grade, MenuDeInteracao menuE)
	{
		Nota("--- B: a bancada de criacao -- a vitrine pelo menu da tecla E, o pente e a tinta de olhos ---");
		const string gi = "Clothes_GiTop", giVermelho = RoupaGuardada.Prefixo + gi + "|FF0000";
		srv.PalcoV21_Limpar(cli.LocalId);
		srv.PalcoV21_Carteira(cli.LocalId, 10_000, 0);
		bool posta = srv.PalcoV21_Bancada(cli.LocalId);
		bool chegou = await Ate(() => cli.Obras.Exists(o => o.Tipo == BancadaDeCriacao.Tipo), 5);
		Checa("(montagem) uma bancada de criacao aparafusada, do jogador, um tile acima dele", posta && chegou);
		if (!posta || !chegou) { srv.PalcoV21_TirarAsObras(cli.LocalId); return; }
		try
		{
			// ---- a vitrine ----
			await Ate(() => menuE.DicaNaTela.Length > 0, 4);
			string dicaDaBancada = menuE.DicaNaTela;
			Toque(Key.E);
			bool abriuOMenu = await Ate(() => menuE.NaTela, 4);
			bool clicou = abriuOMenu && Apertar(menuE, "Fazer uma roupa");
			bool vitrine = await Ate(() => grade.PraVitrineDeTeste, 4);
			(List<string> artes, int comDesenho) = grade.GradeDeTeste();
			Checa("**O MENU DA BANCADA ABRE A VITRINE**: \"Fazer uma roupa...\" fecha o menu e abre a grade do guarda-roupa so com as pecas que tem preco (as do original), cada uma com o desenho -- as luvas, que la nao tem preco, ficam de fora",
				  abriuOMenu && clicou && vitrine && !menuE.NaTela && artes.Count == BancadaDeCriacao.Pecas && comDesenho >= artes.Count - 3
				  && artes.Contains(gi) && !artes.Contains("Clothes_Gloves"),
				  $"dica '{dicaDaBancada}'; menu {abriuOMenu}; clicou {clicou}; vitrine {vitrine}; {artes.Count} pecas ({comDesenho} com desenho) de {BancadaDeCriacao.Pecas}");
			if (vitrine)
			{
				grade.TingirDeTeste(new Color(1, 0, 0));
				bool escolheu = grade.EscolherDeTeste(gi);
				bool veio = await Ate(() => cli.Mochila.Quantos(giVermelho) == 1, 5);
				Checa("**A PECA ESCOLHIDA CHEGA TINGIDA NA MOCHILA, PELO PRECO DELA**: o Gi Top vermelho entra na mochila, cinco zeni saem, e a grade fecha",
					  escolheu && veio && !grade.AbertaDeTeste && Math.Abs(srv.PalcoV21_CarteiraDe(cli.LocalId).Zeni - 9_995) < 0.5,
					  $"escolheu {escolheu}; na mochila {cli.Mochila.Quantos(giVermelho)}; zeni {srv.PalcoV21_CarteiraDe(cli.LocalId).Zeni:0}; grade aberta {grade.AbertaDeTeste}");
			}
			if (grade.AbertaDeTeste) grade.Fechar();
			if (menuE.NaTela) Toque(Key.Escape);
			await Quadros(3);

			// ---- o pente ----
			srv.PalcoV21_Dar(cli.LocalId, BancadaDeCriacao.Pente, 1);
			srv.PalcoV21_Dar(cli.LocalId, BancadaDeCriacao.TintaDeOlhos, 1);
			await Ate(() => cli.Mochila.Quantos(BancadaDeCriacao.Pente) == 1 && cli.Mochila.Quantos(BancadaDeCriacao.TintaDeOlhos) == 1, 4);
			string cabeloDeAntes = srv.PalcoV21_Aparencia(cli.LocalId).Cabelo;
			string novo = cabeloDeAntes == "Raditz" ? "Vegeta" : "Raditz";
			string noBonecoAntes = Boneco()?.CabeloDeTeste ?? "";
			mochila.Abrir();
			await Quadros(3);
			bool abriuOPente = mochila.AbrirAcoesDeTeste(BancadaDeCriacao.Pente) && mochila.ApertarAcaoDeTeste("Mudar o penteado");
			bool pentear = await Ate(() => grade.PraPentearDeTeste, 4);
			mochila.Fechar();
			int penteados = grade.GradeDeTeste().Artes.Count;
			grade.TingirDeTeste(new Color(0, 0, 1));
			bool escolheuOPenteado = pentear && grade.EscolherDeTeste(novo);
			bool trocou = await Ate(() => srv.PalcoV21_Aparencia(cli.LocalId) is { Cabelo: var c, CorDoCabelo: "#0000FF" } && c == novo
										  && (Boneco()?.CabeloDeTeste ?? "") != noBonecoAntes, 5);
			Checa("**O PENTE TROCA O PENTEADO DO BONECO**: a acao da mochila abre a grade com os penteados do catalogo; o escolhido (e a cor marcada) vale no servidor e o boneco troca a folha do cabelo -- e o pente continua na mochila",
				  abriuOPente && pentear && penteados >= 50 && escolheuOPenteado && trocou && cli.Mochila.Quantos(BancadaDeCriacao.Pente) == 1,
				  $"abriu {abriuOPente}; grade {pentear} ({penteados} penteados); escolheu {escolheuOPenteado}; servidor {srv.PalcoV21_Aparencia(cli.LocalId)}; boneco '{noBonecoAntes}' -> '{Boneco()?.CabeloDeTeste}'");
			if (grade.AbertaDeTeste) grade.Fechar();

			// ---- a tinta de olhos ----
			mochila.Abrir();
			await Quadros(3);
			bool abriuATinta = mochila.AbrirAcoesDeTeste(BancadaDeCriacao.TintaDeOlhos) && mochila.ApertarAcaoDeTeste("Tingir os olhos");
			bool olhos = await Ate(() => grade.PraOsOlhosDeTeste, 4);
			mochila.Fechar();
			grade.TingirDeTeste(new Color(0, 1, 0));
			bool apertouATinta = olhos && grade.TingirOsOlhosDeTeste();
			bool tingiu = await Ate(() => srv.PalcoV21_Aparencia(cli.LocalId).CorDoOlho == "#00FF00", 5);
			await Segundos(0.3);
			Checa("**A TINTA TROCA A COR DOS OLHOS**: a acao da mochila abre a tela so com a cor e o botao; confirmada, a cor vale no servidor, o boneco ganha a tinta nos olhos e a tela fecha",
				  abriuATinta && olhos && apertouATinta && tingiu && Boneco()?.TintaDoOlhoDeTeste != null && !grade.AbertaDeTeste,
				  $"abriu {abriuATinta}; tela {olhos}; apertou {apertouATinta}; servidor {srv.PalcoV21_Aparencia(cli.LocalId).CorDoOlho}; boneco {Boneco()?.TintaDoOlhoDeTeste}");
		}
		finally
		{
			if (grade.AbertaDeTeste) grade.Fechar();
			if (menuE.NaTela) Toque(Key.Escape);
			mochila.Fechar();
			srv.PalcoV21_TirarAsObras(cli.LocalId);
			srv.PalcoV21_Limpar(cli.LocalId);
		}
		await Quadros(3);
	}

	// =====================================================================
	// G) O AGARRAO NA TELA (item 36)
	// =====================================================================
	private async Tarefa G_OAgarraoNaTela(GameClient cli, Jandirus.Server.GameServer srv, World mundo, LocalPlayer corpo)
	{
		Nota("--- G: o agarrao na tela -- o aperto planta o corpo e nao soca, o carregado anda junto, o agarrado so vira ---");
		string direita = Teclas.NomeNoMotor("move_right"), esquerda = Teclas.NomeNoMotor("move_left");
		string cima = Teclas.NomeNoMotor("move_up"), baixo = Teclas.NomeNoMotor("move_down");
		string soco = Teclas.NomeNoMotor("attack"), agarrar = Teclas.NomeNoMotor("agarrar");
		Vector2 Onde(int id) => mundo.CorpoDeTeste(id)?.Position ?? Vector2.Zero;

		// Anda um quadro de cada vez por alguns segundos com uma direcao apertada: devolve o quanto o corpo local se
		// afastou de onde estava (o maior valor e o do fim) e a maior distancia vista entre ele e outro corpo.
		async System.Threading.Tasks.Task<(float Maior, float NoFim, float Separados)> Andar(string tecla, double segundos, int outro = 0)
		{
			Vector2 de = Onde(cli.LocalId);
			float maior = 0, separados = 0;
			Input.ActionPress(tecla);
			try
			{
				double fim = Agora() + segundos;
				while (Agora() < fim)
				{
					await Quadros(1);
					maior = Mathf.Max(maior, (Onde(cli.LocalId) - de).Length());
					if (outro != 0) separados = Mathf.Max(separados, (Onde(outro) - Onde(cli.LocalId)).Length());
				}
			}
			finally { Input.ActionRelease(tecla); }
			return (maior, (Onde(cli.LocalId) - de).Length(), separados);
		}

		await Virar(direita);
		int presa = srv.PalcoV21_Presa(cli.LocalId);
		bool chegou = presa != 0 && await Ate(() => mundo.CorpoDeTeste(presa) != null, 5);
		Checa("(montagem) uma presa de bancada, do mesmo poder, de pe na frente do jogador", chegou);
		if (!chegou) { srv.PalcoV21_TirarOFigurante(presa); return; }
		try
		{
			// ---- G1: com alguem na mao o golpe aperta o pescoco -- sem a pose de soco, e com o corpo plantado ----
			await ToqueNaAcao(agarrar);
			bool pegou = await Ate(() => cli.Sheet.Segurando && srv.PalcoV21_Agarrao(cli.LocalId).Quem == presa, 3);
			Checa("(montagem) a tecla de agarrar pegou a presa, pela porta de producao", pegou, $"{srv.PalcoV21_Agarrao(cli.LocalId)}");
			if (!pegou) return;

			Input.ActionPress(soco);
			bool socou = await Ate(() => Boneco()?.EstadoDeTeste == "attack", 0.4);
			Input.ActionRelease(soco);
			bool apertando = await Ate(() => cli.Sheet.Estrangulando && srv.PalcoV21_Agarrao(cli.LocalId).Apertando, 3);
			var (_, andou, _) = await Andar(direita, 0.5);
			string porQue = corpo.PorQueNaoAnda;
			await Quadros(2);
			Checa("**ESTRANGULAR NAO E SOCO, E PLANTA O CORPO**: com a presa na mao o golpe aperta o pescoco sem tocar a pose de soco; apertando, a tecla de andar nao tira o corpo do lugar -- e a presa continua na mao",
				  apertando && !socou && porQue.StartsWith("estrangulando", StringComparison.Ordinal) && andou < 0.5f && srv.PalcoV21_Agarrao(cli.LocalId).Quem == presa,
				  $"apertando {apertando}; pose de soco {socou}; por que nao anda '{porQue}'; andou {andou:0.0} px; servidor {srv.PalcoV21_Agarrao(cli.LocalId)}");

			LocalPlayer.EstranguladorAndaDeTeste = true;
			try
			{
				var (maior, _, _) = await Andar(direita, 0.5);
				Injeta("(quem estrangula volta a integrar a tecla de andar) o corpo sai do lugar na propria tela, e o servidor o devolve a cada pacote -- o corpo tremendo",
					   !corpo.PorQueNaoAnda.StartsWith("estrangulando", StringComparison.Ordinal) && maior > 1f,
					   $"por que nao anda '{corpo.PorQueNaoAnda}'; andou ate {maior:0.0} px");
			}
			finally { LocalPlayer.EstranguladorAndaDeTeste = false; }
			await Segundos(0.6);

			LocalPlayer.EstrangularTocaOSocoDeTeste = true;
			try
			{
				Input.ActionPress(soco);
				bool socouComDefeito = await Ate(() => Boneco()?.EstadoDeTeste == "attack", 0.4);
				Input.ActionRelease(soco);
				Injeta("(o golpe com alguem na mao toca a pose de soco) apertar o pescoco volta a desenhar um soco -- a v0.2.0", socouComDefeito, Boneco()?.EstadoDeTeste ?? "");
			}
			finally { LocalPlayer.EstrangularTocaOSocoDeTeste = false; Input.ActionRelease(soco); }
			await Segundos(0.7);

			// (o aperto alterna a cada golpe: o roteiro segue com o pescoco SOLTO, que e quando a tecla de agarrar levanta)
			if (srv.PalcoV21_Agarrao(cli.LocalId).Apertando)
			{
				await ToqueNaAcao(soco);
				await Ate(() => !srv.PalcoV21_Agarrao(cli.LocalId).Apertando, 2);
				await Segundos(0.4);
			}

			// ---- G2: o segundo toque levanta a presa no colo, e ela anda JUNTO de quem a carrega ----
			await ToqueNaAcao(agarrar);
			bool noColo = await Ate(() => srv.PalcoV21_Agarrao(cli.LocalId) is { NoColo: true, Apertando: false } a && a.Quem == presa, 3);
			await Segundos(0.3);
			var (_, levou, separados) = await Andar(direita, 0.7, presa);
			Checa("**O CARREGADO ANDA JUNTO**: no colo, o corpo carregado e desenhado no ponto de quem o carrega em todo quadro da caminhada -- sem ficar pra tras",
				  noColo && levou > 8f && separados < 0.5f && srv.PalcoV21_Agarrao(cli.LocalId).Quem == presa,
				  $"no colo {noColo}; andei {levou:0.0} px; maior distancia entre os dois {separados:0.00} px; servidor {srv.PalcoV21_Agarrao(cli.LocalId)}");

			RemotePlayer.AgarradoVemAtrasadoDeTeste = true;
			try
			{
				var (_, voltou, atrasado) = await Andar(esquerda, 0.7, presa);
				Injeta("(o corpo no colo vem pela propria interpolacao) andando, ele fica pra tras de quem o carrega -- a v0.2.0",
					   voltou > 8f && atrasado > 2f, $"andei {voltou:0.0} px; ele ficou ate {atrasado:0.0} px pra tras");
			}
			finally { RemotePlayer.AgarradoVemAtrasadoDeTeste = false; }
			await Segundos(0.3);

			await ToqueNaAcao(agarrar);
			bool soltou = await Ate(() => !cli.Sheet.Segurando && srv.PalcoV21_Agarrao(cli.LocalId).Quem == 0, 3);
			Checa("...e o terceiro toque a poe no chao", soltou, $"{srv.PalcoV21_Agarrao(cli.LocalId)}");
		}
		finally
		{
			LocalPlayer.EstranguladorAndaDeTeste = false;
			LocalPlayer.EstrangularTocaOSocoDeTeste = false;
			RemotePlayer.AgarradoVemAtrasadoDeTeste = false;
			Input.ActionRelease(soco);
			Input.ActionRelease(direita);
			Input.ActionRelease(esquerda);
			srv.PalcoV21_TirarOFigurante(presa);
		}
		await Segundos(0.6);

		// ---- G3: agarrado, a tecla de andar so VIRA o corpo ----
		int quem = srv.AgarrarOJogadorDeTeste(cli.LocalId, 50.0, porTras: false);
		Checa("(montagem) um corpo de bancada cinquenta vezes mais forte agarrou o jogador pela frente, pela porta de producao", quem != 0);
		if (quem == 0) return;
		try
		{
			bool soube = await Ate(() => cli.Sheet.Agarrado, 2);
			Facing antes = mundo.OlharLocalDeTeste;
			(string tecla, Facing esperado) = antes == Facing.North ? (baixo, Facing.South) : (cima, Facing.North);
			Vector2 preso = Onde(cli.LocalId);

			LocalPlayer.AgarradoNaoViraDeTeste = true;
			Input.ActionPress(tecla);
			try
			{
				await Segundos(0.4);
				Injeta("(o agarrado nao vira) a direcao apertada conta como luta no servidor e o boneco continua olhando pro mesmo lado -- a v0.2.0",
					   soube && corpo.DebatendoDeTeste && mundo.OlharLocalDeTeste == antes, $"olhar {mundo.OlharLocalDeTeste}");
				LocalPlayer.AgarradoNaoViraDeTeste = false;

				bool virou = await Ate(() => mundo.OlharLocalDeTeste == esperado, 1.5);
				await Segundos(0.3);
				bool pernada = Boneco()?.AndandoDeTeste ?? true;
				float saiu = (Onde(cli.LocalId) - preso).Length();
				Checa("**O AGARRADO QUE SE DEBATE SO VIRA**: a tecla de andar vira o boneco pro lado apertado, sem animacao de andar e sem tirar o corpo do lugar -- e ele continua preso",
					  soube && virou && !pernada && saiu < 0.5f && cli.Sheet.Agarrado,
					  $"olhar {antes} -> {mundo.OlharLocalDeTeste}; animacao de andar {pernada}; saiu {saiu:0.0} px; agarrado {cli.Sheet.Agarrado}");
			}
			finally { Input.ActionRelease(tecla); }
		}
		finally
		{
			LocalPlayer.AgarradoNaoViraDeTeste = false;
			srv.TirarOAgarradorDeTeste(quem);
		}
		await Ate(() => !cli.Sheet.Agarrado, 3);
		await Segundos(0.5);
	}

	// =====================================================================
	// F) AS FORMAS NA TELA (itens 19 e 20)
	// =====================================================================
	private async Tarefa F_AsFormasNaTela(GameClient cli, Jandirus.Server.GameServer srv, World mundo)
	{
		Nota("--- F: as formas na tela -- o cabelo sem a arte de Super Saiyajin doura; o potencial amarela o Namekuseijin; o Orange e laranja e maior ---");
		Jandirus.Core.Forms.FormaDef? ssj = Jandirus.Core.Forms.Catalogo.Def("ssj1");
		Jandirus.Core.Forms.FormaDef? orange = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdOrange);
		string sufixo = ssj?.SufixoDoCabelo ?? "";
		var cabelos = VistosDeGente.Catalogo?.Cabelos ?? [];
		(string Nome, string? Sprite) semArte = cabelos.FirstOrDefault(c => c.Sprite is { Length: > 0 } s && CabelosDeForma.De(s, sufixo) == null);
		(string Nome, string? Sprite) comArte = cabelos.FirstOrDefault(c => c.Sprite is { Length: > 0 } s && CabelosDeForma.De(s, sufixo) != null);
		string? dourado = Jandirus.Core.Forms.Catalogo.CorDoCabeloSemArte(ssj);
		Checa("(montagem) o catalogo tem o Super Saiyajin e o Orange, um penteado SEM a arte de Super Saiyajin e um COM",
			  ssj != null && orange != null && sufixo.Length > 0 && dourado != null && semArte.Nome is { Length: > 0 } && comArte.Nome is { Length: > 0 },
			  $"sem a arte '{semArte.Nome}', com a arte '{comArte.Nome}', sufixo '{sufixo}'");
		if (ssj == null || orange == null || dourado == null || semArte.Nome is not { Length: > 0 } || comArte.Nome is not { Length: > 0 }) return;

		static Vector3 V3(string hexa) { var c = new Color(hexa); return new Vector3(c.R, c.G, c.B); }
		static bool Perto(Vector3 a, Vector3 b) => (a - b).Length() < 0.02f;
		CharacterVisual? Vis(int id) => mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

		int semA = srv.PalcoV21_Figurante(cli.LocalId, "Saiyan", semArte.Nome, 2);
		int comA = srv.PalcoV21_Figurante(cli.LocalId, "Saiyan", comArte.Nome, 3);
		int verde = srv.PalcoV21_Figurante(cli.LocalId, "Namekian", "", 4);
		int desperto = srv.PalcoV21_Figurante(cli.LocalId, "Namekian", "", 5, potencialDesperto: true);
		int apagado = 0;
		try
		{
			bool chegaram = await Ate(() => Vis(semA) is { TemCabeloDeTeste: true } && Vis(comA) is { TemCabeloDeTeste: true }
											&& Vis(verde)?.TintaDoCorpoDeTeste != null && Vis(desperto)?.TintaDoCorpoDeTeste != null, 6);
			Checa("(montagem) quatro figurantes na tela: dois Saiyajins (um penteado de cada) e dois Namekuseijins (um deles de potencial desperto)", chegaram,
				  $"sem a arte {Vis(semA)?.CabeloDeTeste}; com a arte {Vis(comA)?.CabeloDeTeste}");
			if (!chegaram) return;
			await Segundos(0.4);

			// ---- F1: o cabelo ----
			string folhaDeAntes = Vis(comA)!.CabeloDeTeste;
			(Vector3 Tinta, int Modo)? tintaDeAntes = Vis(semA)!.TintaDoCabeloDeTeste;
			bool virou = srv.PalcoV21_Forma(semA, "ssj1") == "ssj1" && srv.PalcoV21_Forma(comA, "ssj1") == "ssj1";
			bool dourou = await Ate(() => Vis(semA)?.TintaDoCabeloDeTeste is { Modo: 0 } t && Perto(t.Tinta, V3(dourado))
										  && Vis(comA)?.CabeloDeTeste is { Length: > 0 } f && f != folhaDeAntes, 4);
			(Vector3 Tinta, int Modo)? daArte = Vis(comA)?.TintaDoCabeloDeTeste;
			Checa("**O PENTEADO SEM A ARTE DE SUPER SAIYAJIN FICA DOURADO**: virando Super Saiyajin, ele continua na mesma folha e ganha o dourado somado; o que TEM a arte troca de folha e nao leva tinta nenhuma",
				  virou && dourou && tintaDeAntes is { } ta && ta.Tinta.Length() < 0.02f && Vis(semA)!.CabeloDeTeste.GetFile().GetBaseName() == semArte.Sprite!.GetFile().GetBaseName()
				  && daArte is { } da && da.Tinta.Length() < 0.02f,
				  $"sem a arte: {tintaDeAntes?.Tinta} -> {Vis(semA)?.TintaDoCabeloDeTeste} na folha {Vis(semA)?.CabeloDeTeste.GetFile()}; com a arte: {folhaDeAntes.GetFile()} -> {Vis(comA)?.CabeloDeTeste.GetFile()}, tinta {daArte?.Tinta}");

			srv.PalcoV21_Forma(semA, "");
			await Ate(() => Vis(semA)?.TintaDoCabeloDeTeste is { } t && t.Tinta.Length() < 0.02f, 3);
			Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = true;
			try
			{
				bool virouDeNovo = srv.PalcoV21_Forma(semA, "ssj1") == "ssj1";
				await Segundos(1.0);
				Injeta("(o cabelo sem arte nao pinta) o Super Saiyajin deixa o penteado sem arte na cor de sempre -- a v0.2.0",
					   virouDeNovo && Vis(semA)?.TintaDoCabeloDeTeste is { } t && t.Tinta.Length() < 0.02f, $"{Vis(semA)?.TintaDoCabeloDeTeste}");
			}
			finally { Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = false; }

			// ---- F2: o potencial desperto na pele do Namekuseijin ----
			(Vector3 Cor, int Modo)? peleVerde = Vis(verde)?.TintaDoCorpoDeTeste, peleDesperta = Vis(desperto)?.TintaDoCorpoDeTeste;
			Checa("**O NAMEKUSEIJIN DE POTENCIAL DESPERTO FICA AMARELADO**: a marca chega na aparencia dele e a pele ganha o vermelho somado (com um pouco menos de azul); o de potencial dormente continua sem tinta",
				  peleVerde is { Modo: 0 } pv && pv.Cor.Length() < 0.02f
				  && peleDesperta is { Modo: 0 } pd && Perto(pd.Cor, V3(Jandirus.Core.Forms.Catalogo.AmareladoDoPotencial)),
				  $"dormente {peleVerde}; desperto {peleDesperta}");

			Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = true;
			try
			{
				apagado = srv.PalcoV21_Figurante(cli.LocalId, "Namekian", "", 6, potencialDesperto: true);
				bool veio = await Ate(() => Vis(apagado)?.TintaDoCorpoDeTeste != null, 5);
				await Segundos(0.5);
				Injeta("(o potencial nao marca a pele) o Namekuseijin de potencial desperto chega a tela verde como qualquer outro -- a v0.2.0",
					   veio && Vis(apagado)?.TintaDoCorpoDeTeste is { } pa && pa.Cor.Length() < 0.02f, $"{Vis(apagado)?.TintaDoCorpoDeTeste}");
			}
			finally { Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = false; }

			// ---- F3: o Orange ----
			float porteDeAntes = Vis(desperto)?.PorteDeTeste ?? 0;
			float porteDoOrange = (float)Jandirus.Core.Forms.Catalogo.PorteDaForma(orange);
			bool noOrange = srv.PalcoV21_Forma(desperto, orange.Id) == orange.Id;
			bool laranja = await Ate(() => Vis(desperto)?.TintaDoCorpoDeTeste is { Modo: 1 } t && Perto(t.Cor, V3(orange.Pele))
										   && Mathf.IsEqualApprox(Vis(desperto)!.PorteDeTeste, porteDoOrange), 4);
			Checa("**NO ORANGE O CORPO FICA LARANJA E MAIOR**: a pele troca pra cor da forma (em matiz) e as camadas do boneco crescem pro porte dela",
				  noOrange && laranja && Mathf.IsEqualApprox(porteDeAntes, 1f) && porteDoOrange > 1.05f,
				  $"pele {Vis(desperto)?.TintaDoCorpoDeTeste} (a da forma: {orange.Pele}); porte {porteDeAntes:0.##} -> {Vis(desperto)?.PorteDeTeste:0.##} (o da forma: {porteDoOrange:0.##})");

			srv.PalcoV21_Forma(desperto, "");
			bool voltou = await Ate(() => Vis(desperto)?.TintaDoCorpoDeTeste is { Modo: 0 } t && Perto(t.Cor, V3(Jandirus.Core.Forms.Catalogo.AmareladoDoPotencial))
										  && Mathf.IsEqualApprox(Vis(desperto)!.PorteDeTeste, 1f), 4);
			Checa("...e sair do Orange devolve a pele amarelada e o tamanho de sempre", voltou, $"pele {Vis(desperto)?.TintaDoCorpoDeTeste}; porte {Vis(desperto)?.PorteDeTeste:0.##}");

			Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = true;
			try
			{
				bool noOrangeDeNovo = srv.PalcoV21_Forma(desperto, orange.Id) == orange.Id;
				await Segundos(1.0);
				Injeta("(a forma nao pinta a pele nem muda o porte) o Orange aparece como um Namekuseijin do tamanho de sempre, sem o laranja -- a v0.2.0",
					   noOrangeDeNovo && Vis(desperto)?.TintaDoCorpoDeTeste is { Modo: 0 } && Mathf.IsEqualApprox(Vis(desperto)!.PorteDeTeste, 1f),
					   $"pele {Vis(desperto)?.TintaDoCorpoDeTeste}; porte {Vis(desperto)?.PorteDeTeste:0.##}");
			}
			finally { Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = false; }
		}
		finally
		{
			Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = false;
			Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = false;
			Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = false;
			foreach (int f in new[] { semA, comA, verde, desperto, apagado }) srv.PalcoV21_TirarOFigurante(f);
		}
		await Segundos(0.5);
	}

	// =====================================================================
	// K) O KI NA TELA (itens 1, 7, 8, 11 e 34)
	// =====================================================================
	private async Tarefa K_OKiNaTela(GameClient cli, Jandirus.Server.GameServer srv, World mundo, LocalPlayer corpo)
	{
		Nota("--- K: o ki na tela -- o som e a pose do tiro, o estouro no fim do alcance, a carga e o som do raio, a tecla X ---");
		const string bola = "Basic_Blast", raio = "Ki_Wave";
		string[] rumos = [Teclas.NomeNoMotor("move_right"), Teclas.NomeNoMotor("move_down"), Teclas.NomeNoMotor("move_left"), Teclas.NomeNoMotor("move_up")];
		string reverter = Teclas.NomeNoMotor("reverter");

		// O QUE A TELA OUVIU, VIU NASCER E VIU ACABAR: o espiao do `AudioDirector` (todo efeito posicionado que de fato
		// tocou, com o volume) e os tres avisos do fio que o `World` tambem escuta.
		var ouvidos = new List<(string Arquivo, float Volume)>();
		var nascidos = new List<Jandirus.Net.NascimentoDeProjetil>();
		var mortos = new List<(int Id, Fim Fim)>();
		var decalques = new List<Jandirus.Net.Protocol.Decal>();
		void AoOuvir(string arquivo, float volume) => ouvidos.Add((arquivo, volume));
		void AoNascer(Jandirus.Net.NascimentoDeProjetil n) { if (n.Dono == cli.LocalId) nascidos.Add(n); }
		void AoMorrer(int id, byte fim, Vec2 onde) => mortos.Add((id, (Fim)fim));
		void AoCair(Jandirus.Net.Protocol.Decal d, Vec2 onde, Facing olhar, Jandirus.Core.Combat.PecaDeCorpo peca) => decalques.Add(d);
		bool Ouviu(string arquivo, float volume = -1) => ouvidos.Exists(o => o.Arquivo == arquivo && (volume < 0 || Mathf.IsEqualApprox(o.Volume, volume)));
		void Esquecer() { ouvidos.Clear(); nascidos.Clear(); mortos.Clear(); decalques.Clear(); }

		Action<string, float>? espiaoDeAntes = AudioDirector.Espiao;
		AudioDirector.Espiao = AoOuvir;
		cli.TiroNasceu += AoNascer;
		cli.TiroMorreu += AoMorrer;
		cli.DecalqueCaiu += AoCair;
		try
		{
			bool armado = srv.PalcoV21_EmprestarVerbos(cli.LocalId, bola, raio);
			Checa("(montagem) o jogador sabe a bola e o raio comuns nesta sessao, com o tanque cheio", armado);
			if (!armado) return;

			// Atira a bola parado, de onde esta e pra onde olha, e espera ela acabar (e o desenho dela chegar ao fim).
			// SEM VIRAR DE NOVO: virar e um toque na direcao, e o toque anda uns pixels -- a bola seguinte nasceria mais
			// adiante, e a que acabava no fim do alcance podia alcancar a pedra logo depois dele (a regressao de 13/10 mediu
			// "acabou como Cenario" na rodada do defeito, com a bola de antes tendo estourado livre no mesmo rumo).
			async System.Threading.Tasks.Task<(bool Nasceu, Fim Fim)> AtirarABola()
			{
				srv.PalcoV21_Ki(cli.LocalId, 1.0);
				Esquecer();
				cli.SendHabilidade(bola);
				if (!await Ate(() => nascidos.Count > 0, 2)) return (false, Fim.Nenhum);
				int id = nascidos[0].Id;
				bool acabou = await Ate(() => mortos.Exists(m => m.Id == id), 10);
				await Segundos(0.7);
				return (true, acabou ? mortos.First(m => m.Id == id).Fim : Fim.Nenhum);
			}

			// ---- K1: a bola -- o som do original em quem atirou, a pose de tiro, e o estouro no fim do alcance ----
			string livre = "";
			bool somDaBola = false, pose = false, primeira = true, nasceuAlguma = false;
			Fim fimDaBola = Fim.Nenhum;
			foreach (string rumo in rumos)
			{
				await Virar(rumo);
				srv.PalcoV21_Ki(cli.LocalId, 1.0);
				Esquecer();
				cli.SendHabilidade(bola);
				bool nasceu = await Ate(() => nascidos.Count > 0, 2);
				nasceuAlguma |= nasceu;
				if (!nasceu) break;
				if (primeira)
				{
					pose = await Ate(() => Boneco()?.EstadoDeTeste == "blast", 0.6);
					somDaBola = Ouviu(Trilha.KiBola, 0.6f) && ouvidos.Count(o => o.Arquivo == Trilha.KiBola) == 1;
					primeira = false;
				}
				int id = nascidos[0].Id;
				bool acabou = await Ate(() => mortos.Exists(m => m.Id == id), 10);
				await Segundos(0.7);
				fimDaBola = acabou ? mortos.First(m => m.Id == id).Fim : Fim.Nenhum;
				if (fimDaBola == Fim.Estourou) { livre = rumo; break; }
				await Segundos(0.6);
			}
			Checa("**O TIRO TEM O SOM E A POSE DELE**: a bola de ki nasce com o `fire_kiblast` do original tocando em quem atirou (uma vez), e o boneco de quem atira veste a pose de tiro",
				  nasceuAlguma && somDaBola && pose, $"nasceu {nasceuAlguma}; som da bola {somDaBola}; pose de tiro {pose}; ouvidos: {Lista(ouvidos)}");
			bool estalo = Ouviu(Trilha.EstaloDeKi, 0.45f), cratera = decalques.Contains(Jandirus.Net.Protocol.Decal.Cratera);
			Checa("**A BOLA QUE NAO ACERTA NINGUEM ESTOURA NO FIM DO ALCANCE**: ela acaba estourando (e nao se apagando), o estalo da explosao toca no ponto, e a cratera pequena chega ao cliente",
				  livre.Length > 0 && fimDaBola == Fim.Estourou && estalo && cratera,
				  $"rumo livre '{livre}'; acabou como {fimDaBola}; estalo {estalo}; cratera {cratera}; decalques [{string.Join(", ", decalques)}]");
			if (livre.Length == 0) livre = rumos[0];

			Jandirus.Core.Combat.KiContraCorpo.BolaSomeAoAcabarDeTeste = true;
			try
			{
				await Segundos(0.5);
				(bool nasceu, Fim fim) = await AtirarABola();
				Injeta("(a bola se apaga no fim do alcance) ela some calada, sem estouro nenhum -- a v0.2.0",
					   nasceu && fim == Fim.Apagou && !Ouviu(Trilha.EstaloDeKi, 0.45f), $"nasceu {nasceu}; acabou como {fim}; ouvidos: {Lista(ouvidos)}");
			}
			finally { Jandirus.Core.Combat.KiContraCorpo.BolaSomeAoAcabarDeTeste = false; }

			Jandirus.Core.Combat.SonsDeKi.TiroSemSomDeTeste = true;
			try
			{
				await Segundos(0.5);
				srv.PalcoV21_Ki(cli.LocalId, 1.0);
				Esquecer();
				cli.SendHabilidade(bola);
				bool nasceu = await Ate(() => nascidos.Count > 0, 2);
				await Segundos(0.4);
				Injeta("(o tiro nasce sem som de ki) a bola sai e nenhum `fire_kiblast` toca", nasceu && !Ouviu(Trilha.KiBola), $"nasceu {nasceu}; ouvidos: {Lista(ouvidos)}");
			}
			finally { Jandirus.Core.Combat.SonsDeKi.TiroSemSomDeTeste = false; }
			await Segundos(2.5);
			srv.LimparOsTirosDaVariedade(cli.LocalId);

			// ---- K2: atirar andando -- a pose do gesto nao planta o corpo ----
			async System.Threading.Tasks.Task<(bool Nasceu, bool Plantou, float Andou)> AtirarAndando()
			{
				await Virar(livre);
				srv.PalcoV21_Ki(cli.LocalId, 1.0);
				Esquecer();
				Vector2 de = mundo.CorpoDeTeste(cli.LocalId)?.Position ?? Vector2.Zero;
				Input.ActionPress(livre);
				try
				{
					await Segundos(0.3);
					cli.SendHabilidade(bola);
					bool nasceu = await Ate(() => nascidos.Count > 0, 2);
					bool plantou = await Ate(() => corpo.PorQueNaoAnda.Length > 0, 0.5);
					return (nasceu, plantou, ((mundo.CorpoDeTeste(cli.LocalId)?.Position ?? de) - de).Length());
				}
				finally { Input.ActionRelease(livre); }
			}
			(bool nasceuAndando, bool plantouAndando, float andouAtirando) = await AtirarAndando();
			Checa("**ATIRAR A BOLA NAO PLANTA O CORPO**: quem atira andando continua andando enquanto a pose de tiro dura -- o gesto nao e um canal de ki",
				  nasceuAndando && !plantouAndando && andouAtirando > 8f, $"nasceu {nasceuAndando}; plantou {plantouAndando}; andou {andouAtirando:0.0} px");
			await Segundos(1.0);
			Jandirus.Server.GameServer.GestoPrendeNaTelaDeTeste = true;
			try
			{
				var (nasceuPreso, plantouPreso, _) = await AtirarAndando();
				Injeta("(a pose do gesto viaja como canal de verdade) quem atira a bola andando trava na propria tela enquanto a pose dura",
					   nasceuPreso && plantouPreso, $"nasceu {nasceuPreso}; por que nao anda '{corpo.PorQueNaoAnda}'");
			}
			finally { Jandirus.Server.GameServer.GestoPrendeNaTelaDeTeste = false; }
			await Segundos(2.5);
			srv.LimparOsTirosDaVariedade(cli.LocalId);

			// ---- K3: o raio -- a carga se ouve; o som do raio anda com ele; no fim do alcance, a explosao e a cratera ----
			async System.Threading.Tasks.Task<bool> Carregar()
			{
				await Virar(livre);
				srv.PalcoV21_Ki(cli.LocalId, 1.0);
				Esquecer();
				cli.SendHabilidade(raio);
				return await Ate(() => srv.CanalDeKiDe(cli.LocalId) is { canal: true, atirando: false }
									   && Todos(mundo).OfType<CargaDeRaioVisual>().Any(), 2.5);
			}

			CargaDeRaioVisual.CargaMudaDeTeste = true;
			try
			{
				bool carregouMudo = await Carregar();
				await Segundos(0.3);
				Injeta("(carregar um raio e mudo) o brilho se junta na mao e nenhum `kame_charge` toca -- a v0.2.0", carregouMudo && !Ouviu(Trilha.CargaDeRaio), $"carregando {carregouMudo}; ouvidos: {Lista(ouvidos)}");
			}
			finally { CargaDeRaioVisual.CargaMudaDeTeste = false; }
			// (a carga do defeito e desfeita -- ou vira raio -- antes da medida de verdade)
			if (srv.CanalDeKiDe(cli.LocalId) is { canal: true, atirando: false }) cli.SendHabilidade(raio);
			await Ate(() => !srv.CanalDeKiDe(cli.LocalId).canal, 14);
			await Segundos(1.5);
			srv.LimparOsTirosDaVariedade(cli.LocalId);
			await Segundos(0.5);

			bool carregou = await Carregar();
			bool cargaOuvida = await Ate(() => Ouviu(Trilha.CargaDeRaio, 0.6f), 1.5);
			bool raioNasceu = await Ate(() => nascidos.Count > 0, 8);
			int idDoRaio = raioNasceu ? nascidos[0].Id : 0;
			await Quadros(3);
			bool somDoRaio = Ouviu(Trilha.KiRaio, 0.7f);
			bool andaComEle = raioNasceu && Todos(mundo).OfType<ProjetilDesenhado>().FirstOrDefault(p => p.Name == $"Tiro{idDoRaio}") is { } noDoRaio
							  && noDoRaio.GetChildren().OfType<AudioStreamPlayer2D>().Any();
			Checa("**A CARGA E O RAIO SE OUVEM, E O SOM DO RAIO ANDA COM ELE**: carregar toca o `kame_charge` do original; o raio nasce com o `kamehameha_fire`, preso no proprio raio (e nao em quem atirou)",
				  carregou && cargaOuvida && raioNasceu && somDoRaio && andaComEle,
				  $"carregando {carregou}; carga ouvida {cargaOuvida}; raio {raioNasceu}; som do raio {somDoRaio}; preso no raio {andaComEle}; ouvidos: {Lista(ouvidos)}");

			bool raioAcabou = raioNasceu && await Ate(() => mortos.Exists(m => m.Id == idDoRaio), 16);
			await Segundos(0.6);
			Fim fimDoRaio = raioAcabou ? mortos.First(m => m.Id == idDoRaio).Fim : Fim.Nenhum;
			Checa("**O RAIO EXPLODE NO FIM DO ALCANCE**: ele acaba explodindo, o estrondo (`kiplosion`) toca no ponto, e a cratera grande chega ao cliente",
				  fimDoRaio == Fim.Explodiu && Ouviu(Trilha.ExplosaoDeKi, 0.95f) && decalques.Contains(Jandirus.Net.Protocol.Decal.CrateraGrande),
				  $"acabou como {fimDoRaio}; estrondo {Ouviu(Trilha.ExplosaoDeKi, 0.95f)}; decalques [{string.Join(", ", decalques)}]");
			if (srv.CanalDeKiDe(cli.LocalId).canal) cli.SendHabilidade(raio);
			await Ate(() => !srv.CanalDeKiDe(cli.LocalId).canal, 5);
			await Segundos(0.5);
			srv.LimparOsTirosDaVariedade(cli.LocalId);

			// ---- K4: a tecla X ----
			async System.Threading.Tasks.Task<bool> LiberarOExcesso()
			{
				srv.PalcoV21_Ki(cli.LocalId, 1.3);
				await Segundos(0.3);
				Esquecer();
				await ToqueNaAcao(reverter);
				return await Ate(() => srv.PalcoV21_RazaoDeKi(cli.LocalId) <= 1.0001, 2);
			}
			bool liberou = await LiberarOExcesso();
			bool descerOuvido = await Ate(() => Ouviu(Trilha.Descer, 0.8f), 1.5);
			Checa("**A TECLA X SE OUVE**: com o Ki acima de 100%, o X o devolve a 100% e o `descend` do original toca em quem apertou",
				  liberou && descerOuvido, $"liberou {liberou} (Ki em {srv.PalcoV21_RazaoDeKi(cli.LocalId):0.00}); ouvidos: {Lista(ouvidos)}");
			srv.XSemSomDeTeste = true;
			try
			{
				bool liberouMudo = await LiberarOExcesso();
				await Segundos(0.8);
				Injeta("(a tecla X muda) o Ki volta a 100% e nada toca -- a v0.2.0", liberouMudo && !Ouviu(Trilha.Descer), $"liberou {liberouMudo}; ouvidos: {Lista(ouvidos)}");
			}
			finally { srv.XSemSomDeTeste = false; }
		}
		finally
		{
			Jandirus.Core.Combat.KiContraCorpo.BolaSomeAoAcabarDeTeste = false;
			Jandirus.Core.Combat.SonsDeKi.TiroSemSomDeTeste = false;
			Jandirus.Server.GameServer.GestoPrendeNaTelaDeTeste = false;
			CargaDeRaioVisual.CargaMudaDeTeste = false;
			srv.XSemSomDeTeste = false;
			foreach (string r in rumos) Input.ActionRelease(r);
			cli.TiroNasceu -= AoNascer;
			cli.TiroMorreu -= AoMorrer;
			cli.DecalqueCaiu -= AoCair;
			AudioDirector.Espiao = espiaoDeAntes;
			srv.LimparOsTirosDaVariedade(cli.LocalId);
			srv.PalcoV21_DevolverVerbos(cli.LocalId);
			srv.PalcoV21_Ki(cli.LocalId, 1.0);
		}
		await Segundos(0.3);
	}

	/// <summary>Os arquivos ouvidos, sem a pasta, com o volume -- pro detalhe de uma linha de placar.</summary>
	private static string Lista(List<(string Arquivo, float Volume)> ouvidos) =>
		ouvidos.Count == 0 ? "(nada)" : string.Join(", ", ouvidos.Select(o => $"{o.Arquivo.GetFile()}@{o.Volume:0.##}"));

	/// <summary>
	/// Um toque numa ACAO do jogo (o golpe, o agarrao, o X), segurado por um decimo de segundo: o corpo le o teclado
	/// perguntando "acabou de apertar?", e um aperto solto no mesmo quadro pode cair entre duas leituras.
	/// </summary>
	private async Tarefa ToqueNaAcao(string acao)
	{
		Input.ActionPress(acao);
		await Segundos(0.1);
		Input.ActionRelease(acao);
		await Quadros(2);
	}

	/// <summary>Vira o corpo local pra um lado com um toque curto na direcao, e espera o servidor ficar sabendo.</summary>
	private async Tarefa Virar(string direcao)
	{
		Input.ActionPress(direcao);
		await Segundos(0.12);
		Input.ActionRelease(direcao);
		await Segundos(0.4);
	}

	private static double Agora() => Time.GetTicksMsec() / 1000.0;

	/// <summary>Aperta, embaixo deste node, o botao visivel cujo texto comeca assim -- o mesmo sinal do dedo.</summary>
	private static bool Apertar(Node raiz, string comecaCom)
	{
		foreach (Node n in Todos(raiz))
		{
			if (n is not Button b || b.Disabled || !b.IsVisibleInTree() || !b.Text.StartsWith(comecaCom, StringComparison.Ordinal)) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	// =====================================================================
	// TECLADO DE MENTIRA, LER A TELA, ESPERAR
	// =====================================================================
	/// <summary>As duas metades preenchidas, como um teclado de verdade manda (a nota da `--diagmudez`).</summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	private static void Toque(Key k) { Tecla(k, true); Tecla(k, false); }

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
