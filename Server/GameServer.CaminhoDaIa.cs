using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>O caminho que um corpo dirigido esta seguindo -- ver `GameServer.DesvioDaIa`. Nasce so quando ha parede no meio.</summary>
public sealed class RotaDaIa
{
	public readonly Vec2[] Pontos = new Vec2[Caminho.MaximoDePontos];

	/// <summary>Quantos pontos valem, e qual e o da vez.</summary>
	public int Quantos, Proximo;

	/// <summary>Pra onde ela foi tracada. O alvo que se afastou disto pede outra.</summary>
	public Vec2 Para;

	/// <summary>Enquanto o relogio nao passar disto, nem se procura: a ultima busca nao achou por onde ir.</summary>
	public long SemCaminhoAte;
}

/// <summary>
/// **A IA CONTORNA A PAREDE** (dono, 2026-10-13: *"n sabe fazer path finding e fica andando contra a parede"*).
///
/// SAO DUAS PECAS, e cada uma resolve um caso:
///
///   1. O DESVIO (<see cref="DesvioDaIa"/>): com um ALVO de verdade e a linha reta barrada, traca-se o caminho
///      (`World.Caminho`, A*) e o cerebro recebe o rumo do proximo passo (`Percepcao.Desvio`). E o que leva o NPC
///      pela porta da casa em vez de pela parede dela.
///   2. O CONTORNO (<see cref="RumoComContorno"/> e <see cref="ContornarSeEmperrou"/>): o passo que emperra --
///      recuando contra um muro, fugindo, vagando, esbarrando em gente -- vira pro lado livre e passa. Vale pra todo
///      comando que o CEREBRO montou (`Comando.DesviaDeParede`), e so pra eles.
///
/// O QUE NAO MUDOU: a agua. O passo barrado pela agua continua sendo assunto da `Travessia` (nadar, voar) -- e so
/// quando ha um caminho pelo seco o corpo a contorna, porque pro A* de quem esta a pe a agua barra como parede.
///
/// O CUSTO: a pergunta "a linha reta serve?" e uma amostra a cada meio tile com a caixa dos pes
/// (`MoveRules.PathOccupied`), feita ~5 vezes por segundo por corpo que tem alvo; a busca so roda quando a resposta
/// e "nao" E a rota guardada nao serve mais. Corpo em campo aberto nao aloca nada (a rota e criada na primeira
/// parede).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>De quanto em quanto tempo um corpo refaz a pergunta do desvio (ms).</summary>
	private const long RelerODesvioMs = 200;

	/// <summary>Sem caminho: espera isto antes de procurar de novo (ms).</summary>
	private const long EsperaSemCaminhoMs = 1500;

	/// <summary>A rota guardada serve enquanto o alvo nao se afastar isto (tiles) de onde ela foi tracada.</summary>
	private const float RotaServeAteTiles = 2f;

	/// <summary>Quantos pontos adiante se tenta alcancar em linha reta (o corte de quina).</summary>
	private const int PontosAdianteDaRota = 8;

	/// <summary>Por quanto tempo o corpo segue o lado que escolheu pra contornar (ms).</summary>
	private const long ContornoMs = 600;

	/// <summary>Quantos caminhos foram tracados neste processo. SO PRA BANCADA.</summary>
	internal int CaminhosTracadosDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o passo que emperra fica emperrado -- o atuador de antes, que
	/// "desliza no eixo livre e para". Com o `Caminho.SemCaminhoDeTeste` junto, e a IA da v0.2.0. Falso em jogo, sempre.
	/// </summary>
	internal static bool PassoNaoContornaDeTeste;

	/// <summary>
	/// O RUMO DO PROXIMO PASSO ATE <paramref name="para"/> QUANDO HA PAREDE NO MEIO; zero quando a linha reta serve
	/// (ou nao ha por onde ir). Chamado a cada tique pela percepcao -- e so trabalha de verdade a cada
	/// <see cref="RelerODesvioMs"/>.
	/// </summary>
	private Vec2 DesvioDaIa(ServerPlayer npc, Vec2 para)
	{
		long agora = NowMs();
		if (agora < npc.DesvioRelerEm) return RumoDaRota(npc);
		// (a fase por id espalha as leituras: corpos nascidos no mesmo tique nao perguntam todos no mesmo quadro)
		npc.DesvioRelerEm = agora + RelerODesvioMs + (npc.Id & 7) * 8;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(npc.Zone);
		if (mapa == null) { LargarARota(npc); return Vec2.Zero; }
		ModoDeTravessia modo = Voo.ModoNaAltura(npc.Altitude, ModoDeTravessiaDe(npc));

		// A LINHA RETA SERVE? Com a caixa dos pes -- a mesma pergunta que o passo vai fazer.
		if (!MoveRules.PathOccupied(mapa, npc.Pos, para, modo)) { LargarARota(npc); return Vec2.Zero; }

		RotaDaIa r = npc.Rota ??= new RotaDaIa();
		bool serve = r.Quantos > 0
					 && Vec2.Distance(r.Para, para) <= RotaServeAteTiles * ZoneCollision.TileSize
					 && !MoveRules.PathOccupied(mapa, npc.Pos, r.Pontos[r.Proximo], modo);
		if (!serve)
		{
			r.Quantos = 0;
			if (agora < r.SemCaminhoAte) return Vec2.Zero;
			int n = Caminho.Achar(mapa, npc.Pos, para, modo, r.Pontos, out _);
			if (n == 0) { r.SemCaminhoAte = agora + EsperaSemCaminhoMs; return Vec2.Zero; }
			r.Quantos = n;
			r.Proximo = 0;
			r.Para = para;
			CaminhosTracadosDeTeste++;
		}

		// O CORTE DE QUINA: o ponto mais adiante que se alcanca em linha reta. O caminho sai em degraus de celula; o
		// corpo anda em linha -- e sem isto ele faria cada esquina em angulo reto, como peca de tabuleiro.
		int ate = Math.Min(r.Quantos, r.Proximo + PontosAdianteDaRota);
		for (int i = r.Proximo + 1; i < ate; i++)
			if (!MoveRules.PathOccupied(mapa, npc.Pos, r.Pontos[i], modo)) r.Proximo = i;

		return RumoDaRota(npc);
	}

	/// <summary>O rumo ate o ponto da vez da rota guardada (passando pro seguinte quando chega nele); zero sem rota.</summary>
	private static Vec2 RumoDaRota(ServerPlayer npc)
	{
		if (npc.Rota is not { Quantos: > 0 } r) return Vec2.Zero;
		const float chegou = 10f;
		while (r.Proximo < r.Quantos - 1 && Vec2.Distance(npc.Pos, r.Pontos[r.Proximo]) < chegou) r.Proximo++;
		Vec2 falta = r.Pontos[r.Proximo] - npc.Pos;
		// NO ULTIMO PONTO: a rota acabou. A proxima leitura decide se a linha reta ja serve.
		if (falta.Length < chegou) { r.Quantos = 0; return Vec2.Zero; }
		return falta.Normalized();
	}

	private static void LargarARota(ServerPlayer npc)
	{
		if (npc.Rota != null) npc.Rota.Quantos = 0;
	}

	// =====================================================================
	// O CONTORNO -- o passo que emperra vira pro lado livre
	// =====================================================================
	/// <summary>O rumo que o corpo anda de verdade: o do cerebro, girado pro lado que ele escolheu enquanto o contorno valer.</summary>
	private Vec2 RumoComContorno(ServerPlayer npc, Vec2 rumoDoCerebro) =>
		!PassoNaoContornaDeTeste && NowMs() < npc.ContornoAte ? Girado(rumoDoCerebro, npc.ContornoAngulo) : rumoDoCerebro;

	/// <summary>
	/// O PASSO EMPERROU? (barrado, e andou menos de um terco do que devia -- quem desliza pela parede NAO emperrou.)
	/// Entao procura-se o rumo livre mais perto do que o cerebro queria: 45, 90 e 135 graus, primeiro pro lado da
	/// ultima vez (trocar de lado a cada batida e o corpo tremendo na frente do muro), e o corpo o segue por
	/// <see cref="ContornoMs"/>.
	/// </summary>
	private void ContornarSeEmperrou(ServerPlayer npc, Vec2 rumoDoCerebro, Vec2 antes, float dt, bool correndo,
									 ZoneCollision mapa, ModoDeTravessia modo)
	{
		if (PassoNaoContornaDeTeste) return;
		float devia = MoveRules.SpeedPx(npc.SpeedStat, correndo) * Math.Min(dt, MoveRules.MaxDeltaSeconds);
		if ((npc.Pos - antes).Length >= devia * 0.34f) return;

		long agora = NowMs();
		if (agora < npc.ContornoAte) return;   // ja esta contornando e emperrou de novo: o prazo decide
		float lado = npc.ContornoLado != 0 ? npc.ContornoLado : (npc.Id & 1) == 0 ? 1f : -1f;
		for (int grau = 45; grau <= 135; grau += 45)
			for (int vez = 0; vez < 2; vez++)
			{
				float angulo = grau * (vez == 0 ? lado : -lado);
				Vec2 sonda = npc.Pos + Girado(rumoDoCerebro, angulo) * (ZoneCollision.TileSize * 0.75f);
				if (MoveRules.PathOccupied(mapa, npc.Pos, sonda, modo)) continue;
				npc.ContornoAngulo = angulo;
				npc.ContornoLado = angulo > 0 ? 1f : -1f;
				npc.ContornoAte = agora + ContornoMs;
				return;
			}
	}

	private static Vec2 Girado(Vec2 v, float graus)
	{
		if (graus == 0) return v;
		float rad = graus * (MathF.PI / 180f);
		float c = MathF.Cos(rad), s = MathF.Sin(rad);
		return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
	}
}
