using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CABECA NA TELA (`--diagcabeca`, 2026-10-27, o pedido 119) ============================
/// A `--cabecateste` prova as regras pelo servidor e nao enxerga a tela. Esta bancada faz o caminho do jogador, no corpo dele, pelas
/// portas do servidor no mesmo processo (`GameServer.FotoDaCabeca.cs`), e fotografa cada passo:
///   1. a cabeca a 50% -- a visao comeca a TURVAR (pouco);
///   2. a cabeca a 25% -- turva e escura quase no maximo;
///   3. derrubado com a cabeca abaixo da linha -- a tela PRETA, e o proprio corpo aparecendo no meio dela;
///   4. de pe, inteiro, derrubado pelo TRONCO ferido -- a tela continua clara, e o "socorro" digitado sai gaguejado no chat;
///   5. o adendo: a cabeca cai abaixo da linha com o corpo ja no chao (o soco na cabeca de quem caiu pelo tronco) -- a tela fica preta;
///   6. de pe de novo -- a cortina sai e o corpo volta ao z de antes.
/// As fotos sao lidas pelo pixel: as laterais da tela (longe do HUD) pretas no desmaio e claras fora dele, e o meio (o corpo) com cor.
///
/// E O PEDIDO 127 (2026-10-27), no meio do caminho: a barra VIDA do HUD e a PIOR parte que derruba (cabeca, torso, abdomen), conferida
/// contra a verdade do servidor (`PiorNucleoNaFoto`) a cada passo -- a cabeca a 50% le ~50%, e nao a media de ~97%; o corpo chega com 1%
/// nos nucleos (a cabeca de 31% a 28%, dentro do mesmo degrau antigo de 4%, move a barra); a foto `cabeca-2b-barra` mostra o traco dos 20%
/// (lido pelo pixel); a Vida do menu P (a barra da aba Stats e a faixa da aba Corpo) segue a do HUD -- o adendo do dono; a virilha conta;
/// e com o cerebro a 30% a foto `cabeca-2c-sem-sentido` mostra a fala trocada no chat.
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 11752 --semfauna --diagcabeca --resolution 1280x720 --raca Human --conta bancada_dcabeca --nome Cabecudo
/// </summary>
public partial class RoboDaCabeca : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private const double Paciencia = 120;

	/// <summary>Quanto esperar a cada passo: o corpo vem na hora, o nocaute vem na ficha, e a tela persegue o alvo.</summary>
	private const double Assentar = 2.0;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou, _falou, _falouSemSentido;
	private double _t, _vida;
	private int _passo, _eu, _zDeAntes;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diagcabeca` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;
		if (mundo.CabecaDeTeste is not { } cabeca) return;

		switch (_passo)
		{
			case 0:
				if (_t < 4) return;   // o mundo monta e o chat assenta
				_eu = cli.LocalId;
				_zDeAntes = mundo.ZDoCorpoLocalDeTeste;
				Fotografar("cabeca-0-inteira.png", "0. a cabeca inteira");
				Conferir(cabeca.TurvaNaTela < 0.01f && !cabeca.DesmaioNaTela, "inteiro: a visao esta limpa", $"turva {cabeca.TurvaNaTela:0.00}");
				srv.FerirNaFoto(_eu, "Cabeca", 0.50);
				Ir(1);
				break;

			case 1:
				if (_t < Assentar) return;
				Fotografar("cabeca-1-meia.png", "1. a cabeca a 50%: comeca a turvar");
				Conferir(cabeca.TurvaNaTela is > 0.15f and < 0.35f, "a cabeca a 50% turva POUCO (~25%)", $"turva {cabeca.TurvaNaTela:0.00}");
				Conferir(BarraSegue(srv, out string barra50) && srv.PiorNucleoNaFoto(_eu) < 0.6,
						 "(o pedido 127) a barra VIDA mostra a CABECA (~50%) -- a pior parte que derruba, e nao a media do corpo (~97%)", barra50);
				srv.FerirNaFoto(_eu, "Cabeca", 0.25);
				Ir(2);
				break;

			case 2:
				if (_t < Assentar) return;
				Fotografar("cabeca-2-quase.png", "2. a cabeca a 25%: turva e escura quase no maximo");
				Conferir(cabeca.TurvaNaTela > 0.8f && !cabeca.DesmaioNaTela, "a cabeca a 25% turva QUASE TUDO (~88%), e de pe nao ha desmaio",
						 $"turva {cabeca.TurvaNaTela:0.00}");
				srv.FerirNaFoto(_eu, "Cabeca", 0.31);
				Ir(10);
				break;

			// ======== (o pedido 127) A BARRA VIDA PELA PIOR PARTE VITAL, e a fala do cerebro ferido ========
			case 10:
				// PASSOS CURTOS (0,4 s) de proposito: a regeneracao natural sobe a cabeca uns decimos por segundo, e o teste de 1% e de 31 pra 28 --
				// o MESMO degrau antigo de 4% (28 a 31), que o corpo de antes nao remandava.
				if (_t < 0.4) return;
				Conferir(BarraSegue(srv, out string barra31), "a cabeca a 31%: a barra segue a verdade do servidor", barra31);
				srv.FerirNaFoto(_eu, "Cabeca", 0.28);
				Ir(11);
				break;

			case 11:
				if (_t < 0.4) return;
				Conferir(BarraSegue(srv, out string barra28),
						 "...e de 31% pra 28% (dentro do degrau de 4% que o corpo de antes nao remandava) a barra DESCE junto: os nucleos chegam com 1%", barra28);
				srv.FerirNaFoto(_eu, "Cabeca", 0.30);
				Ir(12);
				break;

			case 12:
			{
				if (_t < Assentar) return;
				Image? img = Fotografar("cabeca-2b-barra.png", "2b. (o pedido 127) a barra VIDA na cabeca (~30%), com o traco dos 20% (a linha do desmaio)");
				Barra? vida = Hud.Instancia?.BarraDeVida;
				Conferir(BarraSegue(srv, out string barra30), "a cabeca a 30%: a barra VIDA le a cabeca", barra30);
				Conferir(vida != null && Math.Abs(vida.LimiarDeTeste - Jandirus.Core.Combat.Regras.LimiarQuebra) < 0.001,
						 "a barra VIDA tem o TRACO no ponto do desmaio (20% do trilho)", $"traco em {vida?.LimiarDeTeste:0.000}");
				if (img != null && vida != null)
				{
					(double traco, double aoLado) = TracoNaFoto(img, vida.TrilhoNaTelaDeTeste, Jandirus.Core.Combat.Regras.LimiarQuebra);
					Conferir(traco > aoLado + 25, "...e o traco ESTA NA FOTO: a coluna dos 20% e mais clara que o preenchimento ao lado",
							 $"luz {traco:0} no traco, {aoLado:0} ao lado");
				}
				// o menu P, na aba Stats: a Vida dele segue a do HUD (o dono: "Faz o menu P seguir a do HUD")
				MenuJogo.Instancia?.Abrir();
				MenuJogo.Instancia?.IrPara("Stats");
				Ir(15);
				break;
			}

			case 15:
			{
				if (_t < 0.6) return;
				ProgressBar? vidaDoMenu = BarraDoMenu(MenuJogo.Instancia?.PaginaDeTeste("Stats"), "Vida");
				double verdade = srv.PiorNucleoNaFoto(_eu);
				Conferir(vidaDoMenu != null && Math.Abs(vidaDoMenu.Value - verdade) <= 0.006,
						 "(o pedido 127) a Vida do MENU P (aba Stats) segue a do HUD: a cabeca (~30%), e nao a media do corpo",
						 $"menu {vidaDoMenu?.Value:0.000}, servidor {verdade:0.000}");
				MenuJogo.Instancia?.IrPara("Body");
				Ir(16);
				break;
			}

			case 16:
			{
				if (_t < 0.6) return;
				string? faixa = MenuJogo.Instancia?.ValorDesenhado("Body", "Vida");
				string doHud = Hud.Instancia?.BarraDeVida.TextoDeTeste ?? "?";
				Conferir(faixa != null && faixa.StartsWith(doHud + " "),
						 "...e a faixa Vida da aba Corpo tambem: o mesmo numero da barra do HUD", $"corpo \"{faixa}\", HUD \"{doHud}\"");
				MenuJogo.Instancia?.Fechar();
				srv.FerirNaFoto(_eu, "Abdomen", 0.26);
				Ir(13);
				break;
			}

			case 13:
				if (_t < 0.4) return;
				Conferir(BarraSegue(srv, out string barra26) && Hud.Instancia?.BarraDeVida.Valor < 0.285,
						 "a VIRILHA conta: o abdomen a 26% (abaixo da cabeca) e o que a barra mostra", barra26);
				srv.InteiroNaFoto(_eu);
				// 30% e nao 40: o cerebro tambem se costura, e a fronteira exata (40 troca, 41 nao) e da `--cabecateste` (C10)
				srv.FerirNaFoto(_eu, "Cerebro", 0.30);
				Ir(14);
				break;

			case 14:
			{
				if (_t < Assentar) return;
				string frase = $"Hey, {cli.LocalName}, How's the training Going?";
				if (!_falouSemSentido) { _falouSemSentido = true; srv.FalarNaFoto(_eu, frase); return; }
				if (_t < Assentar + 1.5) return;
				Fotografar("cabeca-2c-sem-sentido.png", "2c. (o pedido 127) o cerebro a 30%: a fala digitada chega trocada no chat, com a estrutura e o nome no lugar");
				Conferir(TemNaTela($"Hey, {cli.LocalName}, How's the ") && !TemNaTela("training Going"),
						 $"o \"{frase}\" chegou com as palavras de conteudo TROCADAS, e \"Hey\", o nome e \"How's the\" no lugar");
				srv.InteiroNaFoto(_eu);
				srv.FerirNaFoto(_eu, "Cabeca", 0.15);
				srv.NocautearNaFoto(_eu, 60);
				Ir(3);
				break;
			}

			case 3:
			{
				if (_t < Assentar) return;
				Image? img = Fotografar("cabeca-3-desmaio.png", "3. derrubado com a cabeca abaixo da linha: a tela preta, o corpo no meio");
				var (ko, pelaCabeca) = srv.DesmaioNaFoto(_eu);
				Conferir(ko && pelaCabeca, "o servidor diz: caido, e PELA CABECA", $"KO {ko}, pela cabeca {pelaCabeca}");
				Conferir(cli.Sheet.KO && cabeca.DesmaioNaTela && mundo.VeuDeTeste.Cego, "a tela diz: desmaio pela cabeca, e a cortina preta do veu esta de pe");
				Conferir(mundo.ZDoCorpoLocalDeTeste > CabecaNaTela.ZDoDesmaio, "...e o corpo do jogador esta ACIMA da cortina",
						 $"z do corpo {mundo.ZDoCorpoLocalDeTeste}");
				if (img != null)
				{
					(double esq, double dir) = Laterais(img);
					int noMeio = Acesos(img, mundo.PosicaoLocalDeTeste is { } pos ? mundo.GetViewport().GetCanvasTransform() * pos : null);
					Conferir(esq < 12 && dir < 12, "as laterais da tela estao PRETAS (nao se ve nada do mundo)", $"luz {esq:0.0} e {dir:0.0}");
					Conferir(noMeio > 60, "...e o MEIO tem cor: o proprio corpo aparece", $"{noMeio} pixel(s) acesos no meio");
				}
				srv.InteiroNaFoto(_eu);
				Ir(4);
				break;
			}

			case 4:
				if (_t < Assentar) return;
				srv.FerirNaFoto(_eu, "Torso", 0.40);
				srv.NocautearNaFoto(_eu, 60);
				Ir(5);
				break;

			case 5:
			{
				if (_t < Assentar) return;
				if (!_falou) { _falou = true; srv.FalarNaFoto(_eu, "socorro"); return; }
				if (_t < Assentar + 1.5) return;
				Image? img = Fotografar("cabeca-4-gagueira.png", "4. caido pelo TRONCO ferido: a tela clara, e o \"socorro\" gaguejado no chat");
				var (ko, pelaCabeca) = srv.DesmaioNaFoto(_eu);
				Conferir(ko && !pelaCabeca && !cabeca.DesmaioNaTela, "caido pelo tronco NAO e desmaio pela cabeca: a tela continua clara",
						 $"KO {ko}, pela cabeca {pelaCabeca}");
				if (img != null)
				{
					(double esq, double dir) = Laterais(img);
					Conferir(esq > 20 && dir > 20, "...as laterais mostram o mundo", $"luz {esq:0.0} e {dir:0.0}");
				}
				Conferir(TemNaTela("s-ssoco"), "o \"socorro\" digitado chegou GAGUEJADO no chat (\"s-ssoco...\")");
				Conferir(BarraSegue(srv, out string barraTronco) && srv.PiorNucleoNaFoto(_eu) < 0.45,
						 "(o pedido 127) caido pelo tronco a 40%: a barra VIDA mostra o TRONCO", barraTronco);
				srv.FerirNaFoto(_eu, "Cabeca", 0.15);
				Ir(6);
				break;
			}

			case 6:
			{
				if (_t < Assentar) return;
				Image? img = Fotografar("cabeca-5-adendo.png", "5. o adendo: a cabeca cai abaixo da linha com o corpo ja no chao -- a tela fica preta");
				var (ko, pelaCabeca) = srv.DesmaioNaFoto(_eu);
				Conferir(ko && pelaCabeca && cabeca.DesmaioNaTela, "quem caiu pelo tronco e leva o golpe na cabeca passa a desmaiado PELA CABECA, no servidor e na tela",
						 $"KO {ko}, pela cabeca {pelaCabeca}, tela {cabeca.DesmaioNaTela}");
				if (img != null)
				{
					(double esq, double dir) = Laterais(img);
					Conferir(esq < 12 && dir < 12, "...e as laterais ficaram pretas", $"luz {esq:0.0} e {dir:0.0}");
				}
				srv.InteiroNaFoto(_eu);
				Ir(7);
				break;
			}

			default:
			{
				if (_t < Assentar) return;
				Image? img = Fotografar("cabeca-6-de-pe.png", "6. de pe e inteiro: a cortina saiu");
				Conferir(!cli.Sheet.KO && !cabeca.DesmaioNaTela && !mundo.VeuDeTeste.Cego && mundo.ZDoCorpoLocalDeTeste == _zDeAntes,
						 "de pe: sem desmaio, sem cortina, e o corpo de volta ao z de antes", $"z {mundo.ZDoCorpoLocalDeTeste} (antes {_zDeAntes})");
				if (img != null)
				{
					(double esq, double dir) = Laterais(img);
					Conferir(esq > 20 && dir > 20, "...e as laterais mostram o mundo de novo", $"luz {esq:0.0} e {dir:0.0}");
				}
				Fechar();
				break;
			}
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>
	/// (o pedido 127) A BARRA VIDA DESENHADA E A PIOR PARTE QUE DERRUBA, AGORA, NO SERVIDOR? Ate meio ponto de folga: o fio leva o inteiro
	/// arredondado, e a regeneracao anda uns decimos entre o pacote e a leitura.
	/// </summary>
	private bool BarraSegue(Jandirus.Server.GameServer srv, out string detalhe)
	{
		double naTela = Hud.Instancia?.BarraDeVida.Valor ?? -1, verdade = srv.PiorNucleoNaFoto(_eu);
		detalhe = $"barra {naTela:0.000}, servidor {verdade:0.000}";
		return Math.Abs(naTela - verdade) <= 0.006;
	}

	/// <summary>(o pedido 127) A BARRA DESTE NOME (a meta `barra` que o menu P da a cada barra de vital) numa pagina do menu, em qualquer profundidade.</summary>
	private static ProgressBar? BarraDoMenu(Node? pagina, string barra)
	{
		if (pagina == null) return null;
		foreach (Node f in pagina.GetChildren())
		{
			if (f is ProgressBar b && b.HasMeta("barra") && b.GetMeta("barra").AsString() == barra) return b;
			if (BarraDoMenu(f, barra) is { } achou) return achou;
		}
		return null;
	}

	/// <summary>
	/// (o pedido 127) O TRACO DO LIMIAR NA FOTO: a luz da coluna do traco (no ponto do limiar do trilho, um pixel de folga pra cada lado) e a
	/// do preenchimento cinco pixels ao lado, na linha do meio do trilho.
	/// </summary>
	private static (double Traco, double AoLado) TracoNaFoto(Image img, Rect2 trilho, double limiar)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		int y = Math.Clamp((int)(trilho.Position.Y + trilho.Size.Y / 2), 0, h - 1);
		int x = (int)Math.Round(trilho.Position.X + trilho.Size.X * limiar);
		double Luz(int px)
		{
			Color c = img.GetPixel(Math.Clamp(px, 0, w - 1), y);
			return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * 255;
		}
		return (Math.Max(Luz(x), Math.Max(Luz(x - 1), Luz(x + 1))), (Luz(x - 5) + Luz(x + 5)) / 2);
	}

	/// <summary>A LUZ MEDIA de duas caixas nas laterais da tela, longe do HUD (os quadros de cima, o chat de baixo): 0 = preto.</summary>
	private static (double Esq, double Dir) Laterais(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		double Caixa(float x0)
		{
			double soma = 0;
			int n = 0;
			for (int y = (int)(h * 0.40f); y < (int)(h * 0.55f); y += 3)
				for (int x = (int)(w * x0); x < (int)(w * (x0 + 0.08f)); x += 3)
				{
					Color c = img.GetPixel(x, y);
					soma += (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * 255;
					n++;
				}
			return n == 0 ? 0 : soma / n;
		}
		return (Caixa(0.26f), Caixa(0.70f));
	}

	/// <summary>
	/// Quantos pixels ACESOS (luz acima de 40) ha em volta do corpo do jogador -- onde ele esta NA TELA (a camera presa na beirada do
	/// mapa tira o corpo do meio), ou o meio da tela sem posicao.
	/// </summary>
	private static int Acesos(Image img, Vector2? corpo)
	{
		int w = img.GetWidth(), h = img.GetHeight(), n = 0;
		Vector2 c0 = corpo ?? new Vector2(w / 2f, h / 2f);
		int x0 = Math.Clamp((int)c0.X - 64, 0, w), x1 = Math.Clamp((int)c0.X + 64, 0, w);
		int y0 = Math.Clamp((int)c0.Y - 96, 0, h), y1 = Math.Clamp((int)c0.Y + 48, 0, h);
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				if ((0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * 255 > 40) n++;
			}
		return n;
	}

	/// <summary>ALGUM TEXTO VISIVEL DA TELA (o chat inclusive) TRAZ ESTE TRECHO?</summary>
	private bool TemNaTela(string trecho)
	{
		bool achou = false;
		void Olhar(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (achou) return;
				if (f is CanvasItem ci && !ci.IsVisibleInTree()) continue;
				string? texto = f switch { RichTextLabel r => r.GetParsedText(), Label l => l.Text, _ => null };
				if (texto != null && texto.Contains(trecho, StringComparison.Ordinal)) { achou = true; return; }
				Olhar(f);
			}
		}
		Olhar(GetTree().Root);
		return achou;
	}

	private Image? Fotografar(string nome, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: a janela nao devolveu imagem"); return null; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
		return img;
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		if (S is { } srv && _eu != 0) srv.InteiroNaFoto(_eu);

		GD.Print("\n[dcabeca] ===== A CABECA NA TELA (o pedido 119) =====");
		foreach (string l in _linhas) GD.Print("[dcabeca] " + l);
		GD.Print(_falhas.Count == 0
			? "[dcabeca] ===== TUDO OK ====="
			: $"[dcabeca] ===== {_falhas.Count} FALHA(S) =====\n[dcabeca]   " + string.Join("\n[dcabeca]   ", _falhas));
		GetTree().Quit();
	}
}
