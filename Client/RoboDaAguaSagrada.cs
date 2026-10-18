using System.Globalization;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A AGUA ULTRA DIVINA NA TELA, ATRAS DO BP (`--diagaguasagrada`, 2026-10-16) ============================
/// O dono: *"Agua sagrada do korin mostra o bp mesmo sem scouter, resolva"*. E a regra principal do BP dele: sem medidor ninguem sabe
/// o proprio poder -- nenhum numero, em lugar nenhum.
///
/// A `--aguateste` prova o gole pelo servidor e nao enxerga a tela. Esta bancada faz o caminho do jogador: um corpo SEM scouter vai
/// ao jarro do Mirante (ao lado do Korin), bebe pelo verbo da tecla E, atravessa as fases (o relogio adiantado, as fases andadas pelo
/// tique de producao) e, a cada fase, VARRE A TELA INTEIRA -- todo `Label`, `RichTextLabel` (o chat inclusive), `Button` e `LineEdit`
/// visivel -- atras do numero do BP, antes e depois do gole, nos tres jeitos de escrever (1.234.567, 1,234,567, 1234567). No fim abre
/// o menu P e varre de novo. Qualquer texto com o numero reprova, com o caminho do node e o trecho.
///
/// O BP do corpo e escolhido pra nao se confundir com nenhum outro numero da tela (ver `GameServer.PrepararOGoleNaFoto`).
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio), no segundo monitor e mudo:
///
///     Godot --path . --host --rede 8436 --horateste 0.5 --semfauna --diagaguasagrada --position 1920,0 --resolution 1280x720 \
///           --raca Human --conta bancada_daguasagrada --nome Bebedor
/// </summary>
public partial class RoboDaAguaSagrada : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>O BP do corpo: sete digitos que nao aparecem em nenhum outro lugar da tela.</summary>
	private const double BpDaFoto = 7_654_321;

	private const double Paciencia = 200;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo, _fase;
	private int _eu;

	/// <summary>Todo poder que o corpo teve nesta bancada (base e expresso, antes e depois do gole): e o que a varredura procura.</summary>
	private readonly HashSet<double> _poderes = [];

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	private const int PEntrar = 0, PViajar = 1, PGole = 2, PMenu = 3;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo}.{_fase})"); Fechar(); return; }
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diagaguasagrada` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;

		switch (_passo)
		{
			case PEntrar:
			{
				if (_t < 3) return;
				_eu = cli.LocalId;
				(double bp, double teto) = srv.PrepararOGoleNaFoto(_eu, BpDaFoto);
				var a = srv.AguaNaFoto(_eu);
				Conferir(!a.Scouter, "o corpo NAO tem scouter (a regra que a bancada cobra so vale pra ele)");
				Conferir(bp < teto, $"o corpo esta abaixo do proprio teto: a agua vai dar BP a ele");
				Anotar();
				Conferir(srv.LevarAoJarroNaFoto(_eu), "o corpo foi levado ao jarro do Mirante (ao lado do Korin), pela troca de zona de producao");
				Ir(PViajar);
				break;
			}
			case PViajar:
				if (!cli.Zone.Equals(ZoneKey.Premade("Lookout")) && _t < 15) return;
				if (_t < 4) return;   // o mapa monta e o chat assenta
				Conferir(cli.Zone.Equals(ZoneKey.Premade("Lookout")), "...e esta no Mirante", cli.Zone.ToString());
				Fotografar("dagua-0-jarro.png", "0. ao lado do jarro, antes de beber");
				Varrer("antes de beber");
				cli.SendVerbo(AguaSagrada.VerboBeber);
				Ir(PGole);
				break;
			case PGole:
			{
				if (_t < 1.2) return;
				Anotar();
				Fotografar($"dagua-{1 + _fase}-fase.png", $"{1 + _fase}. o gole, fase {_fase}");
				Varrer($"o gole, fase {_fase}");
				var a = srv.AguaNaFoto(_eu);
				if (!a.Gole)
				{
					Conferir(a.Bebeu, "o gole chegou ao fim e o corpo AGUENTOU (a marca de quem bebeu esta gasta)");
					Conferir(a.Bp > BpDaFoto, "...e o BP subiu de verdade (o que a tela nao pode dizer em numero)", $"{a.Bp:N0}");
					// A FRASE, e nao so o numero: o dono viu a agua contar o poder de luta a quem nao tem scouter (o *"Battle Power has
					// increased"* do DM). Sem medidor, a sensacao -- ver `GameServer.AguaSagrada.DarOQueAAguaDa`.
					Conferir(!TemNaTela("PODER DE LUTA"), "...e sem scouter a tela NAO fala do poder de luta");
					Conferir(TemNaTela("mais forte"), "...so a sensacao: \"você se sente cerca de N% mais forte\"");
					Ir(PMenu);
					break;
				}
				// A PROXIMA FASE: as falas sao de 3 s e o chao, de 30 -- adiantar 31 s anda uma fase por vez.
				srv.AdiantarAAguaDeTeste(31_000);
				_fase++;
				_t = 0;
				if (_fase > 8) { Conferir(false, "o gole acabou em ate oito fases"); Fechar(); }
				break;
			}
			default:
				if (_fase >= 0)
				{
					MenuJogo.Instancia?.Abrir();
					_fase = -1;
					_t = 0;
					return;
				}
				if (_t < 1.5) return;
				Anotar();
				Fotografar("dagua-9-menu.png", "9. o menu P aberto depois do gole");
				Varrer("o menu P, depois do gole");
				MenuJogo.Instancia?.Fechar();
				Fechar();
				break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>Guarda o poder de agora (base e expresso) na lista do que a varredura procura.</summary>
	private void Anotar()
	{
		if (S is not { } srv) return;
		var a = srv.AguaNaFoto(_eu);
		if (a.Bp >= 1000) _poderes.Add(Math.Floor(a.Bp));
		if (a.Expresso >= 1000) _poderes.Add(Math.Floor(a.Expresso));
	}

	// =====================================================================
	// A VARREDURA -- todo texto visivel, atras do numero
	// =====================================================================
	/// <summary>
	/// PROCURA O BP EM TODO TEXTO VISIVEL DA TELA. Os tres jeitos de escrever o numero (o do jogo em portugues, o invariante e o cru),
	/// pra cada poder que o corpo teve. Achou: reprova com o caminho do node e o trecho em volta.
	/// </summary>
	private void Varrer(string quando)
	{
		var alvos = new List<string>();
		foreach (double p in _poderes)
		{
			long n = (long)p;
			alvos.Add(n.ToString("N0", CultureInfo.CurrentCulture));   // o jeito do jogo (o `N0` da maquina: 1.234.567)
			alvos.Add(n.ToString("N0", CultureInfo.InvariantCulture));
			alvos.Add(n.ToString(CultureInfo.InvariantCulture));
		}

		var achados = new List<string>();
		int textos = 0;
		void Olhar(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (f is CanvasItem ci && !ci.IsVisibleInTree()) continue;
				string? texto = f switch
				{
					RichTextLabel r => r.GetParsedText(),
					Label l => l.Text,
					Button b => b.Text,
					LineEdit e => e.Text,
					_ => null,
				};
				if (!string.IsNullOrEmpty(texto))
				{
					textos++;
					foreach (string a in alvos)
					{
						int i = texto.IndexOf(a, StringComparison.Ordinal);
						if (i < 0) continue;
						int de = Math.Max(0, i - 40), ate = Math.Min(texto.Length, i + a.Length + 40);
						achados.Add($"{f.GetPath()}: \"...{texto[de..ate].Replace('\n', ' ')}...\"");
						break;
					}
				}
				Olhar(f);
			}
		}
		Olhar(GetTree().Root);

		Conferir(achados.Count == 0, $"{quando}: nenhum texto da tela mostra o BP ({textos} texto(s) visiveis varridos, {alvos.Count} grafias procuradas)",
				 string.Join(" | ", achados));
	}

	/// <summary>ALGUM TEXTO VISIVEL DA TELA (o chat inclusive) TRAZ ESTE TRECHO? Sem diferenca de caixa.</summary>
	private bool TemNaTela(string trecho)
	{
		bool achou = false;
		void Olhar(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (achou) return;
				if (f is CanvasItem ci && !ci.IsVisibleInTree()) continue;
				string? texto = f switch { RichTextLabel r => r.GetParsedText(), Label l => l.Text, Button b => b.Text, _ => null };
				if (texto != null && texto.Contains(trecho, StringComparison.OrdinalIgnoreCase)) { achou = true; return; }
				Olhar(f);
			}
		}
		Olhar(GetTree().Root);
		return achou;
	}

	// =====================================================================
	// AS FOTOS
	// =====================================================================
	private void Fotografar(string nome, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: a janela nao devolveu imagem"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		if (S is { } srv) srv.SorteioDaAguaDeTeste = null;

		GD.Print("\n[dagua] ===== A AGUA ULTRA DIVINA NA TELA, ATRAS DO BP =====");
		foreach (string l in _linhas) GD.Print("[dagua] " + l);
		GD.Print(_falhas.Count == 0
			? "[dagua] ===== TUDO OK ====="
			: $"[dagua] ===== {_falhas.Count} FALHA(S) =====\n[dagua]   " + string.Join("\n[dagua]   ", _falhas));
		GetTree().Quit();
	}
}
