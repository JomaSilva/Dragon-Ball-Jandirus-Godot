using System.Collections;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA MIRA DO KI (`--diagmira`) -- a metade do CLIENTE do pedido do dono de 2026-10-09: *"ao ter um
/// target e vc usar um ataque de ki o seu personagem ai virar pra direcao do seu target e usar o ataque de ki
/// na direcao do target ... a nao ser q seja teleguiado q ele pode dar curvas"*.
///
/// A regra e as contas estao medidas na `--miradekiteste` (servidor). Aqui se mede o que so a tela responde:
///   * o tiro DESENHADO sai no rumo do marcado (a bola e o raio, na obliqua);
///   * o MEU boneco vira pro alvo na MINHA tela -- o corpo local desenha a propria direcao, e sem o `S2C.Olhar`
///     ele ficaria de costas pro proprio tiro (o defeito injetado mostra);
///   * o raio comum nao acompanha o alvo que saiu, e o teleguiado -- comprado na MESA de verdade, pelo botao --
///     gira atras dele na tela.
///
/// TUDO PELO FIO: o alvo e marcado pelo `C2S.Alvo`, o tiro sai pelo `C2S.Habilidade`, a tecnica e inventada
/// pelos verbos da mesa e pelo botao dela. O servidor do mesmo processo (modo host) so monta a cena -- poe o
/// alvo, anda com ele -- e responde o que ELE acha do tiro e do olhar (`GameServer.FotoDaMira.cs`).
///
/// Rode com janela (no segundo monitor):
///   --host --rede 7986 --campoteste 8 --horateste 0.5 --diagmira --raca Human --conta bancada_mira --nome Atirador
/// </summary>
public partial class RoboDaMiraDeKi : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou;

	/// <summary>O alvo da foto anda isto por segundo (o corpo de foto nao tem pernas: quem o anda e o robo).</summary>
	private Vec2 _passoDoAlvo;
	private int _alvo;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[mira]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[mira]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[mira] " + s);

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		if (_alvo != 0 && _passoDoAlvo.LengthSquared > 0)
			S?.AndarOAlvoDaMira(_alvo, _passoDoAlvo * (float)delta);

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext()) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		LocalPlayer.OlharDoServidorIgnoradoDeTeste = false;
		GD.Print($"\n[mira] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[mira] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	private Image? Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	private static double Graus(Vector2 desenhado, Vec2 esperado) =>
		MeleeArea.Angulo(new Vec2(desenhado.X, desenhado.Y), esperado);

	private static ProjetilDesenhado? NoDoTiro(World mundo, int id) =>
		id == 0 ? null : mundo.FindChild($"Tiro{id}", recursive: true, owned: false) as ProjetilDesenhado;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		World mundo = M!;
		if (S is not { } srv) { Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false); yield break; }
		int eu = cli.LocalId;

		srv.EmbateDeFoto_CeuLimpo(eu);
		(int skills, int verbos) = srv.ArmarParaAVariedade(eu);
		srv.EsquecerAsInventadasDaVariedade(eu);
		srv.RegarOKiDaVariedade(eu);
		Nota($"o corpo sabe {skills} skills e {verbos} verbos");
		yield return 1.0;

		// ------------------------------------------------------------ 1) a bola no marcado da obliqua
		Nota("-- 1) a bola sai no marcado, e o meu boneco vira --");
		var onde = new Vec2(7, 4);
		_alvo = srv.AlvoDaFotoDaMira(eu, onde);
		yield return 0.8;
		cli.SendAlvo(_alvo);
		yield return 0.4;
		Afirmar("(montagem) o alvo esta na tela, marcado pelo `C2S.Alvo`, e o meu boneco ainda nao olha pra ele",
				mundo.PosicaoDoAlvo != null && mundo.OlharLocalDeTeste != Facing.East, $"olhando {mundo.OlharLocalDeTeste}");

		Vec2 esperado = onde.Normalized();
		cli.SendHabilidade("Basic_Blast");
		ProjetilDesenhado? no = null;
		var tiro = srv.TiroDaMira(eu);
		for (int i = 0; i < 20 && no == null; i++)
		{
			yield return 0.04;
			tiro = srv.TiroDaMira(eu);
			no = NoDoTiro(mundo, tiro.Id);
		}
		Afirmar("no servidor a bola nasceu no rumo EXATO do marcado (7 tiles a leste, 4 ao sul)",
				tiro.Achou && MeleeArea.Angulo(tiro.Rumo, esperado) < 0.5, $"rumo {tiro.Rumo}, esperado {esperado}");
		yield return 0.08;
		Afirmar("...e na TELA ela voa nesse rumo -- nao e uma bola indo pro leste",
				no != null && IsInstanceValid(no) && Graus(no.Rumo, esperado) < 4,
				no == null ? "o no do tiro nao apareceu" : $"{Graus(no.Rumo, esperado):0.0} graus do rumo do alvo");
		Afirmar("o MEU boneco virou pro lado do alvo na minha tela (leste), e o servidor diz o mesmo",
				mundo.OlharLocalDeTeste == Facing.East && srv.CorpoDaMira(eu).Olhar == Facing.East,
				$"tela {mundo.OlharLocalDeTeste}, servidor {srv.CorpoDaMira(eu).Olhar}");
		Fotografar("mira-01-a-bola-sai-no-marcado");
		yield return 1.2;
		Afirmar("passado o prazo da pose, sem tecla nenhuma, as duas pontas continuam de acordo",
				mundo.OlharLocalDeTeste == Facing.East && srv.CorpoDaMira(eu).Olhar == Facing.East,
				$"tela {mundo.OlharLocalDeTeste}, servidor {srv.CorpoDaMira(eu).Olhar}");

		// ------------------------------------------------------------ 1b) o contra-exemplo da metade de ca
		srv.LimparOsTirosDaVariedade(eu);
		srv.PorOAlvoDaMira(eu, _alvo, new Vec2(-7, 2));
		yield return 0.6;
		LocalPlayer.OlharDoServidorIgnoradoDeTeste = true;
		cli.SendHabilidade("Basic_Blast");
		yield return 0.25;
		Afirmar("(defeito injetado: o corpo local ignora o olhar do servidor) o tiro sai pro oeste e a MINHA tela me mostra de costas pra ele",
				mundo.OlharLocalDeTeste == Facing.East && MeleeArea.Angulo(srv.TiroDaMira(eu).Rumo, new Vec2(-7, 2)) < 0.5,
				$"tela {mundo.OlharLocalDeTeste}, rumo do tiro {srv.TiroDaMira(eu).Rumo}");
		Fotografar("mira-01b-defeito-de-costas-pro-proprio-tiro");
		LocalPlayer.OlharDoServidorIgnoradoDeTeste = false;
		yield return 1.0;
		srv.LimparOsTirosDaVariedade(eu);
		cli.SendHabilidade("Basic_Blast");
		yield return 0.25;
		Afirmar("sem o defeito, o mesmo tiro pro oeste vira o meu boneco pro oeste",
				mundo.OlharLocalDeTeste == Facing.West && srv.CorpoDaMira(eu).Olhar == Facing.West,
				$"tela {mundo.OlharLocalDeTeste}, servidor {srv.CorpoDaMira(eu).Olhar}");
		yield return 1.0;

		// ------------------------------------------------------------ 2) o raio comum
		Nota("-- 2) o raio comum: mira na carga, sai na obliqua e nao acompanha quem sai --");
		srv.LimparOsTirosDaVariedade(eu);
		srv.RegarOKiDaVariedade(eu);
		onde = new Vec2(5, -4);
		srv.PorOAlvoDaMira(eu, _alvo, onde);
		yield return 0.6;
		cli.SendHabilidade("Ki_Wave");
		yield return 0.25;
		Afirmar("ao COMECAR a carga o meu boneco ja encara o marcado (leste)",
				mundo.OlharLocalDeTeste == Facing.East && srv.CorpoDaMira(eu).Olhar == Facing.East,
				$"tela {mundo.OlharLocalDeTeste}, servidor {srv.CorpoDaMira(eu).Olhar}");

		tiro = srv.TiroDaMira(eu);
		for (int i = 0; i < 60 && !tiro.Achou; i++) { yield return 0.05; tiro = srv.TiroDaMira(eu); }
		yield return 0.2;
		tiro = srv.TiroDaMira(eu);
		no = NoDoTiro(mundo, tiro.Id);
		esperado = onde.Normalized();
		Afirmar("a carga fechou e o raio nasceu no rumo do marcado (5 tiles a leste, 4 ao norte)",
				tiro.Achou && MeleeArea.Angulo(tiro.Rumo, esperado) < 0.5 && tiro.Alvo == 0, $"rumo {tiro.Rumo}, esperado {esperado}");
		Afirmar("...e na TELA o feixe sai da mao NA OBLIQUA, ate o alvo",
				no != null && IsInstanceValid(no) && Graus(no.Rumo, esperado) < 3,
				no == null ? "o no do raio nao apareceu" : $"{Graus(no.Rumo, esperado):0.0} graus do rumo do alvo");
		Fotografar("mira-02-o-raio-sai-na-obliqua");

		Vec2 rumoDoRaio = tiro.Rumo;
		srv.PorOAlvoDaMira(eu, _alvo, new Vec2(5, 4));   // o alvo sai da frente: passa pro outro lado
		yield return 0.6;
		tiro = srv.TiroDaMira(eu);
		no = NoDoTiro(mundo, tiro.Id);
		Afirmar("o alvo saiu e o raio COMUM nao foi atras: mesmo rumo no servidor e na tela",
				tiro.Achou && MeleeArea.Angulo(tiro.Rumo, rumoDoRaio) < 1e-4 && no != null && IsInstanceValid(no) && Graus(no.Rumo, rumoDoRaio) < 3,
				$"rumo {tiro.Rumo} (era {rumoDoRaio})");
		Fotografar("mira-03-o-alvo-saiu-e-o-raio-comum-nao-virou");
		cli.SendHabilidade("Ki_Wave");   // apertar de novo solta
		yield return 0.8;
		srv.LimparOsTirosDaVariedade(eu);

		// ------------------------------------------------------------ 3) o raio teleguiado, pela mesa
		Nota("-- 3) o raio teleguiado: comprado no botao da mesa, gira atras do alvo --");
		TelaDeTecnicas? tela = TelaDeTecnicas.Instancia;
		if (tela == null) { Afirmar("(montagem) a tela de tecnicas existe", false); yield break; }
		tela.Abrir();
		cli.SendVerbo("ca_criar");
		yield return 0.8;
		string botao = $"Teleguiado: o raio acompanha quem você marcou ({TecnicaCustomizada.PrecoDoTeleguiado} pontos)";
		Afirmar("a mesa de um RAIO novo oferece o botao do teleguiado, e ele foi apertado",
				cli.Mesa is { Tipo: TipoDeProjetil.Beam, Teleguiado: false } && tela.ClicarBotao(botao),
				cli.Mesa == null ? "mesa fechada" : $"{cli.Mesa.Tipo}, teleguiado {cli.Mesa.Teleguiado}");
		yield return 0.6;
		Afirmar($"o servidor ligou o teleguiado e cobrou {TecnicaCustomizada.PrecoDoTeleguiado} pontos -- e a mesa mostra",
				cli.Mesa is { Teleguiado: true, Gasto: TecnicaCustomizada.PrecoDoTeleguiado },
				cli.Mesa == null ? "mesa fechada" : $"teleguiado {cli.Mesa.Teleguiado}, gasto {cli.Mesa.Gasto}");
		Fotografar("mira-04-a-mesa-com-o-botao-do-teleguiado");
		Afirmar("o botao de criar existe e foi apertado", tela.ClicarBotao("Criar a técnica"));
		yield return 0.6;
		(string verbo, int gasto) = srv.RaioTeleguiadoDaMira(eu);
		Afirmar("o raio teleguiado ficou guardado no corpo, com os pontos que custou",
				verbo.Length > 0 && gasto == TecnicaCustomizada.PrecoDoTeleguiado, $"verbo '{verbo}', gasto {gasto}");
		tela.ClicarBotao("Fechar (Esc)");
		if (verbo.Length == 0) yield break;

		srv.RegarOKiDaVariedade(eu);
		onde = new Vec2(9, 0);
		srv.PorOAlvoDaMira(eu, _alvo, onde);
		yield return 0.6;
		cli.SendHabilidade(verbo);
		tiro = srv.TiroDaMira(eu);
		for (int i = 0; i < 80 && !tiro.Achou; i++) { yield return 0.05; tiro = srv.TiroDaMira(eu); }
		Afirmar("o raio inventado nasceu perseguindo o marcado", tiro.Achou && tiro.Alvo == _alvo, $"alvo do tiro {tiro.Alvo} (o marcado e {_alvo})");

		_passoDoAlvo = new Vec2(0, 90);   // o alvo anda pro sul, 2,8 tiles por segundo
		yield return 0.3;
		_passoDoAlvo = default;
		yield return 0.1;
		tiro = srv.TiroDaMira(eu);
		no = NoDoTiro(mundo, tiro.Id);
		Afirmar("com o alvo andando de lado o raio GIROU atras dele no servidor (o rumo saiu do da saida)",
				tiro.Achou && Teleguiado.Desvio(tiro.Rumo, tiro.Saida) > 1.5,
				$"girou {Teleguiado.Desvio(tiro.Rumo, tiro.Saida):0.0} graus");
		Afirmar("...e na TELA o feixe acompanha: reto, da mao ate a ponta, no rumo novo",
				no != null && IsInstanceValid(no) && Graus(no.Rumo, tiro.Rumo) < 3,
				no == null ? "o no do raio nao apareceu" : $"{Graus(no.Rumo, tiro.Rumo):0.0} graus do rumo do servidor");
		Fotografar("mira-05-o-raio-teleguiado-girou-atras-do-alvo");

		cli.SendHabilidade(verbo);   // solta
		yield return 0.8;
		srv.LimparOsTirosDaVariedade(eu);
		srv.EsquecerAsInventadasDaVariedade(eu);
		yield return 0.3;
	}
}
