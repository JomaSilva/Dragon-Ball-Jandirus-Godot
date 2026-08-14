using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// UM ATAQUE DE KI NA TELA -- e ele NAO decide nada.
///
/// ============================ ELE E UM ESPELHO, E SO ISSO ============================
/// Nao integra posicao, nao testa colisao, nao sabe quanto doeu e nao adivinha aonde o tiro vai
/// parar. Recebe a posicao da CABECA (e, no raio, o fim do rastro) do servidor a 30 Hz e desliza
/// ate ela. Se o servidor calar, ele para -- que e a leitura honesta de "perdi o pacote".
///
/// A INTERPOLACAO E DE DESENHO, NAO DE VERDADE: ela existe porque 30 Hz de posicao num objeto que
/// anda a 320 px/s da saltos de 10 px, e o olho pega. O ponto que ACERTA e sempre o do servidor;
/// este node pode estar meio quadro atras dele, e e por isso que a explosao vem no pacote de morte
/// (com a posicao certa) em vez de sair daqui quando o sprite "chega".
/// ====================================================================================
///
/// ============================ POR QUE DESENHO E NAO SPRITE ============================
/// O ki do DM e um `.dmi` colorido por `rgb()` em cima de uma folha cinza -- a mesma receita da
/// aura. Aqui a bola e um circulo com halo e o raio e uma faixa com nucleo claro, porque isso
/// escala pra QUALQUER comprimento de rastro sem esticar arte: um beam de 30 tiles nao tem sprite,
/// tem 960 px de faixa. Quando houver folha propria, ela entra por cima -- o formato do dado
/// (cabeca + cauda) nao muda.
/// ================================================================================
/// </summary>
public partial class ProjetilDesenhado : Node2D
{
	/// <summary>Raio da bola, em pixels. Meio tile -- o mesmo <c>Projetil.RaioDeImpacto</c>.</summary>
	private const float RaioDaBola = Projetil.RaioDeImpacto;

	/// <summary>Meia largura do raio. Mais fino que a bola: um beam corta, uma bola estoura.</summary>
	private const float MeiaLarguraDoRaio = 9f;

	/// <summary>
	/// Quanto do erro se fecha por segundo. Alto o bastante pra nao "arrastar" atras do servidor
	/// (o tiro e rapido e o atraso apareceria como o efeito nascendo longe da mao), baixo o
	/// bastante pra tirar o serrilhado dos 30 Hz.
	/// </summary>
	private const float Suavizacao = 22f;

	public TipoDeProjetil Tipo;

	/// <summary>A cor do ki de QUEM ATIROU -- ver <see cref="Aura.CorDaFicha"/>. Nao vem no pacote.</summary>
	public Color Cor = Aura.CorDoKiCru;

	private Vector2 _cabecaAlvo, _caudaAlvo;
	private Vector2 _cabeca, _cauda;
	private bool _primeiro = true;

	/// <summary>O servidor falou: e para AQUI que o tiro esta indo.</summary>
	public void Mirar(Vector2 cabeca, Vector2 cauda)
	{
		_cabecaAlvo = cabeca;
		_caudaAlvo = cauda;

		// O PRIMEIRO PACOTE NAO SE INTERPOLA. Sem isto todo tiro nasceria na origem do mundo e
		// voaria ate a mao do dono -- um risco atravessando o mapa, uma vez por disparo.
		if (!_primeiro) return;
		_cabeca = cabeca;
		_cauda = cauda;
		_primeiro = false;
	}

	public override void _Process(double delta)
	{
		float t = Mathf.Min(1f, (float)delta * Suavizacao);
		_cabeca = _cabeca.Lerp(_cabecaAlvo, t);
		_cauda = _cauda.Lerp(_caudaAlvo, t);

		// A POSICAO DO NODE E A CABECA, e o desenho e em coordenadas locais: assim o Y-sort do
		// `Atores` ordena o tiro pelo ponto que de fato importa (onde ele vai acertar), e nao pelo
		// meio de um rastro que pode ter trinta tiles.
		Position = _cabeca;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Color nucleo = Cor.Lightened(0.55f);
		Color halo = Cor with { A = 0.35f };

		if (Tipo == TipoDeProjetil.Beam)
		{
			Vector2 fim = _cauda - _cabeca;   // local: a cabeca e a origem
			DrawLine(Vector2.Zero, fim, halo, MeiaLarguraDoRaio * 2.6f);
			DrawLine(Vector2.Zero, fim, Cor, MeiaLarguraDoRaio * 1.6f);
			DrawLine(Vector2.Zero, fim, nucleo, MeiaLarguraDoRaio * 0.7f);

			// A PONTA E MAIS GORDA que o corpo do raio -- e a "cabeca" do `KHH()`, e e o que deixa
			// claro, olhando de longe, para que lado o beam esta indo.
			DrawCircle(Vector2.Zero, MeiaLarguraDoRaio * 1.5f, halo);
			DrawCircle(Vector2.Zero, MeiaLarguraDoRaio, nucleo);
			return;
		}

		DrawCircle(Vector2.Zero, RaioDaBola * 1.7f, halo);
		DrawCircle(Vector2.Zero, RaioDaBola, Cor);
		DrawCircle(Vector2.Zero, RaioDaBola * 0.45f, nucleo);
	}
}
