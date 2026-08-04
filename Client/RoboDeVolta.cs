using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA VOLTA DO PLANETA (`--diagvolta`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// A volta e um TELEPORTE de 16.000 px disparado por um passo. Olhando, ve-se o personagem sumir
/// de uma beirada e aparecer na outra -- e nada disso diz:
///   * o corpo terminou do lado CERTO, ou foi parar dentro de uma parede?
///   * o servidor e o cliente concordam depois do salto, ou o corpo fica tremendo na costura?
///   * a volta acontece ANTES do chao indestrutivel, ou o jogador encosta numa parede invisivel?
///   * andar de volta pro mesmo lado da a volta OUTRA vez, ou a costura so funciona uma vez?
/// ==================================================================================
///
/// COMO RODAR:
///     Godot --path . --host --voltateste --diagvolta --nome Volta --conta volta
///
/// O `--voltateste` faz nascer a seis tiles da beirada OESTE -- sem ele o teste andaria 249 tiles
/// (100 segundos) antes de chegar na costura.
/// </summary>
public partial class RoboDeVolta : Node
{
	/// <summary>Quanto tempo esperar a volta antes de desistir.</summary>
	private const double Paciencia = 25;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _fase;
	private double _t, _espera;
	private float _xInicial, _xDepois;
	private int _voltas;
	private float _larguraDoMapa;
	private double _tremorAcumulado;
	private Vector2 _ultima;

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		// CADA CORRECAO conta: depois de um teleporte legitimo elas param. Se continuarem, o
		// cliente e o servidor estao brigando pela costura -- que e o modo de falha que um wrap mal
		// feito produz, e o unico que nao aparece numa foto.
		if (C is { } cli) cli.Corrected += _ => _correcoes++;
		GD.Print("[volta] andando pro oeste ate o fim do mundo...");
	}

	private int _correcoes;

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (mundo.PosicaoLocal is not { } onde) return;

		_t += delta;
		if (_espera > 0) { _espera -= delta; return; }

		switch (_fase)
		{
			case 0:
				// mede o tamanho do mapa pela colisao que o cliente ja carregou
				_larguraDoMapa = mundo.LarguraDoMapaDeTeste * Jandirus.Core.World.ZoneCollision.TileSize;
				_xInicial = onde.X;
				Conferir(_larguraDoMapa > 0, $"o cliente conhece o tamanho do mapa ({_larguraDoMapa:0} px)");
				Conferir(_xInicial < _larguraDoMapa * 0.25f,
					$"a bancada comeca perto da beirada OESTE (x={_xInicial:0})");
				_correcoes = 0;
				_fase = 1;
				break;

			case 1:
				// ANDA PRO OESTE. Nao ha atalho: o passo tem que ser o do jogador, senao o que se
				// testa e o atalho.
				mundo.AndarDeTeste(new Vector2(-1, 0));
				if (onde.X > _larguraDoMapa * 0.5f)
				{
					// saltou pra outra metade: a volta aconteceu
					_xDepois = onde.X;
					_voltas++;
					// PARA DE ANDAR pra a proxima fase medir o corpo QUIETO. Sem isto o piloto
					// automatico continua indo pro oeste e a "deriva parado" mede caminhada.
					mundo.PararDeTeste();
					_fase = 2;
					_espera = 1.0;   // deixa a poeira baixar antes de medir tremor
					break;
				}
				if (_t > Paciencia)
				{
					Conferir(false, $"a volta NAO aconteceu em {Paciencia:0}s (parou em x={onde.X:0})");
					_fase = 4;
				}
				break;

			case 2:
				Conferir(true, $"deu a VOLTA: x {_xInicial:0} -> {_xDepois:0} (mapa {_larguraDoMapa:0} px)");
				Conferir(_xDepois > _larguraDoMapa * 0.8f,
					$"apareceu do lado OPOSTO, e nao no meio ({_xDepois / _larguraDoMapa:P0} do mapa)");
					_correcoes = 0;
				mundo.PararDeTeste();
				_ultima = onde;
				_fase = 3;
				_espera = 2.0;   // dois segundos parado, contando correcao e deriva
				break;

			case 3:
			{
				// A COSTURA NAO PODE TREMER. Depois do salto o corpo tem que ficar quieto: correcao
				// atras de correcao e o sintoma do wrap que briga com a validacao de passo.
				Conferir(_correcoes <= 2,
					$"o corpo NAO fica tremendo na costura: {_correcoes} correcao(oes) em 2s parado");
				_tremorAcumulado = onde.DistanceTo(_ultima);
				Conferir(_tremorAcumulado < 8,
					$"e nao deriva sozinho: {_tremorAcumulado:0.0} px de deslocamento parado");

				// E DA A VOLTA DE NOVO, pro outro lado: uma costura que so funciona uma vez e uma
				// costura quebrada.
				_t = 0;
				_fase = 5;
				break;
			}

			case 5:
				mundo.AndarDeTeste(new Vector2(1, 0));
				if (onde.X < _larguraDoMapa * 0.5f)
				{
					_voltas++;
					Conferir(true, $"deu a volta PELO OUTRO LADO tambem (x={onde.X:0})");
					_fase = 4;
					break;
				}
				if (_t > Paciencia)
				{
					Conferir(false, $"a volta pelo LESTE nao aconteceu em {Paciencia:0}s (x={onde.X:0})");
					_fase = 4;
				}
				break;

			default:
				_acabou = true;
				Conferir(_voltas >= 2, $"as duas voltas aconteceram ({_voltas})");
				GD.Print("\n[volta] ===== BANCADA DA VOLTA DO PLANETA =====");
				foreach (string l in _passos) GD.Print("[volta] " + l);
				GD.Print(_falhas.Count == 0
					? "[volta] ===== TUDO OK ====="
					: $"[volta] ===== {_falhas.Count} FALHA(S) =====\n[volta]   " + string.Join("\n[volta]   ", _falhas));
				break;
		}
	}
}
