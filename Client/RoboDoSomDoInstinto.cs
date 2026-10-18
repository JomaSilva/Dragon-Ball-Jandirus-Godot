using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SOM DO ULTRA INSTINTO (`--diagsomui`, 2026-10-17). O dono: *"dps da primeira cinematica do ultra instinto, sempre q se
/// transformar em qualquer forma do ultra instinto (pressagio ou perfeito) vai tocar esse audio"* (`UltraInstinctSoundEffect.ogg`).
///
/// Headless, com `--host`. O corpo local muda de forma pelo `World.AoMudarForma` -- a porta por onde o pacote do servidor entra, e
/// nao uma chamada ao tocador -- e o que TOCOU de verdade e contado pelo espiao do `AudioDirector` (ele so ouve o que carregou). As
/// cenas que o caminho acende sao descartadas na hora, como no `RoboDeForma.AIdaEAVolta`.
///
///   1. o nome do som resolve para o arquivo, e o arquivo esta importado;
///   2. o Sign INSTANTANEO (maestria dominada) toca o som do instinto, e nao o swoosh;
///   3. o Perfected com a cena ENCURTADA tambem toca;
///   4. a ESTREIA nao toca (ela e a cena cheia, com o tema da forma);
///   5. uma forma de fora da linha (o SSJ1 instantaneo) continua com o swoosh;
///   6. o pacote de ESTADO (de X pra X: quem chega numa zona) nao toca nada;
///   7. CONTRA-EXEMPLO (`Cinematicas.SomDoInstintoMudoDeTeste`): o Sign instantaneo volta ao swoosh.
///
///     Godot --path . --headless --host --rede 8448 --campoteste 8 --semfauna --diagsomui --raca Saiyan --conta bancada_dsomui --nome Instinto
/// </summary>
public partial class RoboDoSomDoInstinto : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _tocados = [];
	private bool _acabou;
	private double _espera;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true, LocalId: > 0 } cli || World.Instancia is not { PosicaoLocalDeTeste: not null } mundo
			|| GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo)
		{
			_espera += delta;
			if (_espera > 30) { Conferir(false, "em 30 s o cliente conectou e o corpo local nasceu"); Fechar(); }
			return;
		}
		_acabou = true;
		Rodar(mundo, cli.LocalId, corpo);
		Fechar();
	}

	private void Rodar(World mundo, int eu, Node2D corpo)
	{
		GD.Print("[somui] ===== O SOM DO ULTRA INSTINTO (dono, 2026-10-17) =====");
		string? caminho = Transformacao.CaminhoDoSom(Cinematicas.SomDoUltraInstinto);
		Conferir(caminho == Trilha.UltraInstinto && ResourceLoader.Exists(Trilha.UltraInstinto),
					$"o som `{Cinematicas.SomDoUltraInstinto}` resolve para o arquivo, e ele esta importado ({caminho ?? "nulo"})");

		AudioDirector.Espiao = (c, _) => _tocados.Add(c);
		try
		{
			string b = Catalogo.IdBase;
			List<string> Mudar(string de, string para, DegrauDeCena degrau)
			{
				var antes = new HashSet<Transformacao>(Cenas(corpo));
				_tocados.Clear();
				mundo.AoMudarForma(eu, Catalogo.Rede(de), Catalogo.Rede(para), degrau);
				foreach (Transformacao t in Cenas(corpo))
					if (!antes.Contains(t)) t.Free();
				return [.. _tocados];
			}
			bool Instinto(List<string> s) => s.Contains(Trilha.UltraInstinto);
			bool Swoosh(List<string> s) => s.Contains(Trilha.Dash);
			string Lista(List<string> s) => s.Count == 0 ? "nada" : string.Join(", ", s.Select(x => System.IO.Path.GetFileName(x)));

			List<string> s1 = Mudar(b, "ui_sign", DegrauDeCena.Nenhuma);
			Conferir(Instinto(s1) && !Swoosh(s1), $"o Sign INSTANTANEO (maestria dominada) toca o som do instinto, e nao o swoosh ({Lista(s1)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			List<string> s2 = Mudar(b, "ui_perfected", DegrauDeCena.Curta);
			Conferir(Instinto(s2), $"o Perfected com a cena ENCURTADA tambem toca ({Lista(s2)})");
			Mudar("ui_perfected", b, DegrauDeCena.Nenhuma);

			List<string> s3 = Mudar(b, "ui_sign", DegrauDeCena.Estreia);
			Conferir(!Instinto(s3), $"a ESTREIA (a cena cheia, com o tema da forma) nao toca o som por cima ({Lista(s3)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			List<string> s4 = Mudar(b, "ssj1", DegrauDeCena.Nenhuma);
			Conferir(Swoosh(s4) && !Instinto(s4), $"uma forma de fora da linha (o SSJ1 instantaneo) continua com o swoosh ({Lista(s4)})");
			Mudar("ssj1", b, DegrauDeCena.Nenhuma);

			Mudar(b, "ui_sign", DegrauDeCena.Nenhuma);
			List<string> s5 = Mudar("ui_sign", "ui_sign", DegrauDeCena.Nenhuma);
			Conferir(s5.Count == 0, $"o pacote de ESTADO (de Sign pra Sign: quem chega numa zona) nao toca nada ({Lista(s5)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			Cinematicas.SomDoInstintoMudoDeTeste = true;
			List<string> s6;
			try { s6 = Mudar(b, "ui_sign", DegrauDeCena.Nenhuma); }
			finally { Cinematicas.SomDoInstintoMudoDeTeste = false; }
			Conferir(!Instinto(s6) && Swoosh(s6), $"CONTRA-EXEMPLO (defeito injetado: o instinto sem som proprio): o Sign instantaneo volta ao swoosh ({Lista(s6)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);
		}
		finally { AudioDirector.Espiao = null; }
	}

	private static List<Transformacao> Cenas(Node2D corpo) =>
		[.. corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid)];

	private void Fechar()
	{
		_acabou = true;
		Cinematicas.SomDoInstintoMudoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[somui] " + l);
		GD.Print(_falhas.Count == 0 ? "[somui] ===== TUDO OK =====" : $"[somui] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[somui]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
