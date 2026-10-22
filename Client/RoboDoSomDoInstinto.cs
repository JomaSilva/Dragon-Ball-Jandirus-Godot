using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SOM DO ULTRA INSTINTO (`--diagsomui`, 2026-10-17). O dono: *"dps da primeira cinematica do ultra instinto, sempre q se
/// transformar em qualquer forma do ultra instinto (pressagio ou perfeito) vai tocar esse audio"* (`UltraInstinctSoundEffect.ogg`).
///
/// E O SOM DAS OUTRAS INSTANTANEAS (2026-10-20). O dono: *"o som de transformar instantaneamente quando a maestria e acima de 50%
/// deveria ser o mesmo som inicial q faz ao apertar C"*. Era o swoosh do dash; virou o estalo de comecar a carga. A regua o compara
/// com o que o C TOCA de verdade -- o `CargaVisual.Som` do corpo local, ouvido pelo mesmo espiao --, e nao com uma constante: "o
/// mesmo som" e arquivo E volume, e o par sai da porta do C.
///
/// Headless, com `--host`. O corpo local muda de forma pelo `World.AoMudarForma` -- a porta por onde o pacote do servidor entra, e
/// nao uma chamada ao tocador -- e o que TOCOU de verdade e contado pelo espiao do `AudioDirector` (ele so ouve o que carregou). As
/// cenas que o caminho acende sao descartadas na hora, como no `RoboDeForma.AIdaEAVolta`.
///
///   1. o nome do som resolve para o arquivo, e o arquivo esta importado;
///   2. o C toca o estalo de comecar a carga (`chargeaura.wav`) -- a referencia das linhas de baixo;
///   3. o Sign INSTANTANEO (maestria dominada) toca o som do instinto, e nem o estalo nem o swoosh;
///   4. o Perfected com a cena ENCURTADA tambem toca;
///   5. a ESTREIA nao toca (ela e a cena cheia, com o tema da forma);
///   6. uma forma de fora da linha (o SSJ1 instantaneo) toca o MESMO estalo do C, e nao o swoosh;
///   7. o pacote de ESTADO (de X pra X: quem chega numa zona) nao toca nada;
///   8. CONTRA-EXEMPLO (`Cinematicas.SomDoInstintoMudoDeTeste`): o Sign instantaneo volta ao estalo do C;
///   9. CONTRA-EXEMPLO (`World.InstantaneaComSwooshDeTeste`): com o swoosh de antes, a regua do 6 REPROVA.
///
///     Godot --path . --headless --host --rede 8448 --campoteste 8 --semfauna --diagsomui --raca Saiyan --conta bancada_dsomui --nome Instinto
/// </summary>
public partial class RoboDoSomDoInstinto : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<(string Arquivo, float Volume)> _tocados = [];
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
		GD.Print("[somui] ===== O SOM DO ULTRA INSTINTO (dono, 2026-10-17) E O DAS OUTRAS INSTANTANEAS (2026-10-20) =====");
		string? caminho = Transformacao.CaminhoDoSom(Cinematicas.SomDoUltraInstinto);
		Conferir(caminho == Trilha.UltraInstinto && ResourceLoader.Exists(Trilha.UltraInstinto),
					$"o som `{Cinematicas.SomDoUltraInstinto}` resolve para o arquivo, e ele esta importado ({caminho ?? "nulo"})");

		AudioDirector.Espiao = (c, v) => _tocados.Add((c, v));
		try
		{
			string Lista(List<(string Arquivo, float Volume)> s) =>
				s.Count == 0 ? "nada" : string.Join(", ", s.Select(x => $"{System.IO.Path.GetFileName(x.Arquivo)} a {x.Volume:0.00}"));

			// O QUE O C TOCA, pela porta dele: o `Som(true)` e quem solta o estalo quando o servidor confirma a carga. O laco que vem
			// junto tem tocador proprio e nao passa pelo espiao, entao o que sobra na lista e o estalo e so ele. Desligado em seguida --
			// tudo aqui roda num quadro so, e nenhum pacote entra no meio.
			(string Arquivo, float Volume)? doC = null;
			List<(string Arquivo, float Volume)> c0 = [];
			if (corpo.GetNodeOrNull<CargaVisual>("Carga") is { } carga)
			{
				_tocados.Clear();
				carga.Som(true);
				c0 = [.. _tocados];
				carga.Som(false);
				if (c0.Count == 1) doC = c0[0];
			}
			Conferir(doC is { } refC && refC.Arquivo == Trilha.CargaInicio,
					 $"o C (o `CargaVisual.Som` do corpo local) toca um estalo so, o de comecar a carga `chargeaura.wav` ({Lista(c0)})");

			string b = Catalogo.IdBase;
			List<(string Arquivo, float Volume)> Mudar(string de, string para, DegrauDeCena degrau)
			{
				var antes = new HashSet<Transformacao>(Cenas(corpo));
				_tocados.Clear();
				mundo.AoMudarForma(eu, Catalogo.Rede(de), Catalogo.Rede(para), degrau);
				foreach (Transformacao t in Cenas(corpo))
					if (!antes.Contains(t)) t.Free();
				return [.. _tocados];
			}
			bool Instinto(List<(string Arquivo, float Volume)> s) => s.Any(x => x.Arquivo == Trilha.UltraInstinto);
			bool Swoosh(List<(string Arquivo, float Volume)> s) => s.Any(x => x.Arquivo == Trilha.Dash);
			// O MESMO SOM E O PAR INTEIRO: o arquivo do C tocado mais alto (ou mais baixo) que o C ja seria outro som pro ouvido.
			bool EstaloDoC(List<(string Arquivo, float Volume)> s) => doC is { } c && s.Contains(c);
			// A REGUA DA INSTANTANEA COMUM, uma so pra linha 6 e pro contra-exemplo 9.
			bool SoaComoOC(List<(string Arquivo, float Volume)> s) => EstaloDoC(s) && !Swoosh(s) && !Instinto(s);

			List<(string Arquivo, float Volume)> s1 = Mudar(b, "ui_sign", DegrauDeCena.Nenhuma);
			Conferir(Instinto(s1) && !EstaloDoC(s1) && !Swoosh(s1),
					 $"o Sign INSTANTANEO (maestria dominada) toca o som do instinto, e nem o estalo do C nem o swoosh ({Lista(s1)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			List<(string Arquivo, float Volume)> s2 = Mudar(b, "ui_perfected", DegrauDeCena.Curta);
			Conferir(Instinto(s2), $"o Perfected com a cena ENCURTADA tambem toca ({Lista(s2)})");
			Mudar("ui_perfected", b, DegrauDeCena.Nenhuma);

			List<(string Arquivo, float Volume)> s3 = Mudar(b, "ui_sign", DegrauDeCena.Estreia);
			Conferir(!Instinto(s3), $"a ESTREIA (a cena cheia, com o tema da forma) nao toca o som por cima ({Lista(s3)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			List<(string Arquivo, float Volume)> s4 = Mudar(b, "ssj1", DegrauDeCena.Nenhuma);
			Conferir(SoaComoOC(s4),
					 $"**O SSJ1 INSTANTANEO TOCA O ESTALO DO C**: uma forma de fora da linha do instinto soa como o C (mesmo arquivo, mesmo volume), e nao com o swoosh do dash ({Lista(s4)})");
			Mudar("ssj1", b, DegrauDeCena.Nenhuma);

			Mudar(b, "ui_sign", DegrauDeCena.Nenhuma);
			List<(string Arquivo, float Volume)> s5 = Mudar("ui_sign", "ui_sign", DegrauDeCena.Nenhuma);
			Conferir(s5.Count == 0, $"o pacote de ESTADO (de Sign pra Sign: quem chega numa zona) nao toca nada ({Lista(s5)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			Cinematicas.SomDoInstintoMudoDeTeste = true;
			List<(string Arquivo, float Volume)> s6;
			try { s6 = Mudar(b, "ui_sign", DegrauDeCena.Nenhuma); }
			finally { Cinematicas.SomDoInstintoMudoDeTeste = false; }
			Conferir(!Instinto(s6) && EstaloDoC(s6),
					 $"CONTRA-EXEMPLO (defeito injetado: o instinto sem som proprio): **o Sign instantaneo volta ao ESTALO DO C** ({Lista(s6)})");
			Mudar("ui_sign", b, DegrauDeCena.Nenhuma);

			World.InstantaneaComSwooshDeTeste = true;
			List<(string Arquivo, float Volume)> s7;
			try { s7 = Mudar(b, "ssj1", DegrauDeCena.Nenhuma); }
			finally { World.InstantaneaComSwooshDeTeste = false; }
			Conferir(!SoaComoOC(s7) && Swoosh(s7),
					 $"CONTRA-EXEMPLO (defeito injetado: a instantanea com o swoosh de antes de 2026-10-20): a mesma regua do SSJ1 REPROVA ({Lista(s7)})");
			Mudar("ssj1", b, DegrauDeCena.Nenhuma);
		}
		finally { AudioDirector.Espiao = null; }
	}

	private static List<Transformacao> Cenas(Node2D corpo) =>
		[.. corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid)];

	private void Fechar()
	{
		_acabou = true;
		Cinematicas.SomDoInstintoMudoDeTeste = false;
		World.InstantaneaComSwooshDeTeste = false;
		foreach (string l in _linhas) GD.Print("[somui] " + l);
		GD.Print(_falhas.Count == 0 ? "[somui] ===== TUDO OK =====" : $"[somui] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[somui]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
