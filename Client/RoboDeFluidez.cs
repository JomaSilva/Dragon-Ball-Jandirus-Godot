using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A FLUIDEZ DO CORPO REMOTO ============================
/// O dono, palavra por palavra: *"movimentacao de outros jogadores remotos como andando, correndo
/// ou voando nao esta fluida, eles parecem ficar dando micro teleportes e quando o player e muito
/// rapido fica mais perceptivel"*.
///
/// O que se mede e o PASSO POR QUADRO DA POSICAO DESENHADA de um `RemotePlayer` -- o `Desenhar` de
/// producao, depois da grade de desenho. Um corpo liso tem passos parelhos (desvio-padrao pequeno
/// em relacao a media), nenhum quadro com o dobro do passo mediano (o "micro teleporte") e nenhum
/// quadro parado com o corpo andando (o congelamento que o precede).
///
/// ============================ DOIS BERCOS ============================
///   `--diagfluidez`     LABORATORIO, sem mundo e sem rede. Um `RemotePlayer` de producao recebe
///                       snapshots SINTETICOS de um corpo a 352 e a 1760 px/s, com jitter de
///                       chegada, dois pacotes no mesmo quadro, quadro sem pacote, o degrau do tique
///                       (posicao repetida + deslocamento dobrado), corpo movido pelo servidor, voo
///                       e teleporte, avancando o `_Process` a 60 Hz com delta irregular. Tem
///                       CONTRA-EXEMPLO (o teleporte de 3000 px crava no mesmo quadro) e DEFEITO
///                       INJETADO (`RemotePlayer.DefeitoCarimboDeChegada`: a mesma alimentacao TEM
///                       que reprovar carimbando pela hora de chegada -- senao a bancada nao enxerga).
///   `--fluidez a|b`     DOIS PROCESSOS e corpo de verdade. `a` anda, corre e voa em pernas retas
///                       segurando as acoes reais (`Input.ActionPress`); `b` grava, quadro a quadro,
///                       o passo do `RemotePlayer` de `a` e aplica os mesmos criterios.
///
/// O laboratorio e o juiz MAIS FORTE do desenho (ele sabe a posicao VERDADEIRA do corpo a cada
/// quadro, entao mede distancia total e latencia) e o mais fraco do fio (ele forja o servidor). Os
/// dois processos sao o contrario: nao sabem a verdade, mas atravessam `SendState`, o `Input` do
/// servidor, o snapshot e o `World`. Um sem o outro aceitaria o remedio errado.
/// ====================================================================================
///
/// COMO RODAR:  testar-fluidez.bat  (as tres rodadas) -- ou, uma por vez:
///     Godot --headless --path . --diagfluidez
///     Godot --headless --path . --host --rede 7971 --kiteste --bpteste 100000 --vooteste --fluidez a --raca Human --conta bancada_fluidez_a --nome FluidezA
///     Godot --headless --path . --rede 7971 --connect 127.0.0.1 --fluidez b --fluidezalvo FluidezA --raca Human --conta bancada_fluidez_b --nome FluidezB
/// </summary>
public partial class RoboDeFluidez : Node
{
	/// <summary>Nasci antes do lobby e forjo o servidor -- ver "dois bercos".</summary>
	[Export] public bool Laboratorio;

	/// <summary>`a` anda, `b` olha. Vem do `--fluidez`.</summary>
	public string Papel = "";

	/// <summary>O nome de quem o B olha (`--fluidezalvo`).</summary>
	public string Alvo = "";

	/// <summary>Rotulo do relatorio em disco -- duas rodadas escrevem no MESMO `user://`.</summary>
	public string Rotulo = "";

	/// <summary>`--fluidezsaida CAMINHO`: copia do relatorio fora do `user://`.</summary>
	public string Saida = "";

	/// <summary>
	/// Quantos segundos o B grava, contados de quando ele avista o A. O roteiro do A dura 31 s
	/// (3 de espera + 28 de pernas) e ele so fecha 6 s depois: o B TEM que dar o veredito antes de
	/// o host cair, senao o placar morre junto com a conexao.
	/// </summary>
	public double Duracao = 33;

	// =====================================================================
	// OS CRITERIOS -- os mesmos nos dois bercos
	// =====================================================================
	/// <summary>Desvio-padrao / media da velocidade por quadro, no maximo. Acima disso o passo e irregular a olho.</summary>
	private const double DesvioMaximo = 0.15;

	/// <summary>Um quadro com esta vez a velocidade mediana e um micro teleporte.</summary>
	private const double FatorDeSalto = 2.0;

	/// <summary>Quanto se ignora depois de o corpo comecar a andar, em quadros (o atraso de desenho + o arranque).</summary>
	private const int QuadrosDeArranque = 15;

	private readonly List<string> _linhas = [];
	private int _ok, _falhas;

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Nota(string s)
	{
		_linhas.Add(s);
		GD.Print("[fluidez] " + s);
	}

	private void Checa(string nome, bool ok, string detalhe)
	{
		if (ok) _ok++; else _falhas++;
		Nota($"  {(ok ? "ok  " : "FALHA")} {nome} -- {detalhe}");
	}

	private void Fechar()
	{
		Nota(_falhas == 0
			? $"===== {_ok} OK, NENHUMA FALHA ====="
			: $"===== {_ok} OK, {_falhas} FALHA(S) =====");

		string nome = $"user://fluidez-{(Rotulo.Length > 0 ? Rotulo : Laboratorio ? "lab" : Papel)}.txt";
		try
		{
			using var f = Godot.FileAccess.Open(nome, Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(string.Join("\n", _linhas) + "\n");
			if (Saida.Length > 0) File.WriteAllText(Saida, string.Join("\n", _linhas) + "\n");
		}
		catch (Exception ex) { GD.PushWarning($"[fluidez] nao gravei o relatorio: {ex.Message}"); }

		// ELA SAI SOZINHA, com um segundo de folga pro log sair inteiro -- o mesmo desfecho das
		// outras bancadas em lote (ver `RoboDeCarga.Fechar`).
		if (GetTree() is { } arv) arv.CreateTimer(1.0).Timeout += () => GetTree()?.Quit(_falhas == 0 ? 0 : 1);
	}

	// =====================================================================
	// A MEDIDA -- uma serie de passos por quadro vira um veredito
	// =====================================================================
	/// <summary>Um quadro gravado: quanto tempo ele durou, quando foi (relogio de quadro) e onde o corpo foi DESENHADO.</summary>
	private readonly record struct Quadro(double DeltaMs, double LocalMs, Vector2 Desenhada, Vector2 Exata, float Altura,
										  bool Movendo, Facing Olhar, double AtrasoMs, int Inanicoes);

	private sealed class Medida
	{
		public int Quadros;
		public double Mediana, Media, Desvio, MediaExata, DesvioExato;
		public int Saltos, Congelamentos, SaltosExatos;
		public double Distancia;
	}

	/// <summary>
	/// A ESTATISTICA DA VELOCIDADE POR QUADRO. Velocidade e nao passo: o delta e irregular de
	/// proposito (e assim que o jogo roda), e o passo certo de um quadro de 25 ms e maior que o de um
	/// de 14. Um quadro liso tem a MESMA velocidade dos vizinhos, nao o mesmo passo.
	///
	/// `Saltos` conta quadros com velocidade acima de <see cref="FatorDeSalto"/> vezes a mediana --
	/// o micro teleporte. `Congelamentos` conta passo ZERO com o corpo andando.
	/// </summary>
	private static Medida Medir(IReadOnlyList<Quadro> quadros)
	{
		var m = new Medida();
		var vel = new List<double>();
		var velExata = new List<double>();
		for (int i = 1; i < quadros.Count; i++)
		{
			double passo = (quadros[i].Desenhada - quadros[i - 1].Desenhada).Length();
			double passoExato = (quadros[i].Exata - quadros[i - 1].Exata).Length();
			double dt = quadros[i].DeltaMs / 1000.0;
			if (dt <= 0) continue;
			m.Distancia += passo;
			vel.Add(passo / dt);
			velExata.Add(passoExato / dt);
			if (passo <= 0) m.Congelamentos++;
		}
		m.Quadros = vel.Count;
		if (vel.Count == 0) return m;

		var ordenada = vel.OrderBy(v => v).ToList();
		m.Mediana = ordenada[ordenada.Count / 2];
		m.Media = vel.Average();
		m.Desvio = Math.Sqrt(vel.Sum(v => (v - m.Media) * (v - m.Media)) / vel.Count);
		m.MediaExata = velExata.Average();
		m.DesvioExato = Math.Sqrt(velExata.Sum(v => (v - m.MediaExata) * (v - m.MediaExata)) / velExata.Count);
		m.Saltos = vel.Count(v => v >= FatorDeSalto * m.Mediana);
		m.SaltosExatos = velExata.Count(v => v >= FatorDeSalto * m.Mediana);
		return m;
	}

	private static string Resumo(Medida m)
		=> $"{m.Quadros} quadros | vel media {m.Media:0} px/s, mediana {m.Mediana:0}, desvio/media {(m.Media > 0 ? m.Desvio / m.Media : 0):0.000} "
		   + $"(exata {(m.MediaExata > 0 ? m.DesvioExato / m.MediaExata : 0):0.000}) | saltos >= {FatorDeSalto}x: {m.Saltos} (exata {m.SaltosExatos}) | congelamentos: {m.Congelamentos}";

	/// <summary>Os tres criterios, com o nome do cenario na linha. Devolve se TODOS passaram.</summary>
	private bool Julgar(string cenario, Medida m, bool esperaPassar = true)
	{
		bool liso = m.Quadros >= 30 && m.Media > 0 && m.Desvio / m.Media < DesvioMaximo;
		bool semSalto = m.Quadros >= 30 && m.Saltos == 0;
		bool semCongelar = m.Quadros >= 30 && m.Congelamentos == 0;
		bool tudo = liso && semSalto && semCongelar;
		if (esperaPassar)
		{
			Checa($"{cenario}: passo parelho (desvio/media < {DesvioMaximo})", liso, Resumo(m));
			Checa($"{cenario}: nenhum quadro com passo >= {FatorDeSalto}x a mediana", semSalto, $"{m.Saltos} salto(s)");
			Checa($"{cenario}: nenhum congelamento com o corpo andando", semCongelar, $"{m.Congelamentos} quadro(s) parado(s)");
		}
		return tudo;
	}

	// =====================================================================
	// O LABORATORIO -- o servidor forjado
	// =====================================================================
	/// <summary>
	/// O JUIZ DA VIRADA. O corpo local vira e sai andando na direcao nova NO MESMO QUADRO; o remoto tem
	/// que fazer o mesmo -- o quadro em que o OLHAR desenhado vira pro norte e o quadro em que o PASSO
	/// desenhado passa a ser vertical sao o mesmo (com um quadro de folga), e nenhum quadro mostra o
	/// corpo virado pro norte ainda deslizando pra leste. Devolve se passou.
	/// </summary>
	private bool AVirada(string cenario, Rodada r, bool esperaPassar = true)
	{
		int quadroDaVirada = -1, quadroDoPasso = -1, deslizandoVirado = 0;
		for (int i = 1; i < r.Quadros.Count; i++)
		{
			Vector2 passo = r.Quadros[i].Desenhada - r.Quadros[i - 1].Desenhada;
			bool andouNorte = passo.Length() > 0.01f && Math.Abs(passo.Y) > Math.Abs(passo.X);
			bool andouLeste = passo.Length() > 0.01f && Math.Abs(passo.X) >= Math.Abs(passo.Y);
			if (quadroDaVirada < 0 && r.Quadros[i].Olhar == Facing.North) quadroDaVirada = i;
			if (quadroDoPasso < 0 && andouNorte) quadroDoPasso = i;
			if (r.Quadros[i].Olhar == Facing.North && andouLeste) deslizandoVirado++;
		}
		// UM quadro virado com o passo ainda mais pra leste e o QUADRO DA ESQUINA: o desenho corta a curva
		// dentro de um unico quadro (a reta entre a ultima amostra a leste e a primeira ao norte), e nesse
		// quadro o passo e misto -- a 352 px/s, 3 px pra cada lado. O corpo local nao tem esse quadro porque
		// le a tecla no comeco do quadro e anda o quadro inteiro na direcao nova; o remoto nao sabe em que
		// instante do tique a tecla mudou. Dois ou mais e deslize de verdade (o defeito injetado da 5).
		bool ok = quadroDaVirada >= 0 && quadroDoPasso >= 0 && Math.Abs(quadroDaVirada - quadroDoPasso) <= 1 && deslizandoVirado <= 1;
		string detalhe = $"olhar virou no quadro {quadroDaVirada}, o passo dobrou no quadro {quadroDoPasso}, {deslizandoVirado} quadro(s) deslizando virado pro norte";
		if (esperaPassar) Checa($"{cenario}: o olhar vira no MESMO quadro em que o passo dobra (folga de 1), sem deslizar virado alem do quadro da esquina", ok, detalhe);
		else Nota($"  (defeito) {cenario}: {detalhe} -> {(ok ? "passou" : "reprovou, como devia")}");
		return ok;
	}

	private sealed class Cenario
	{
		public string Nome = "";
		public double PxPorSegundo = 352;
		public double JitterMs = 10;
		/// <summary>O corpo e movido pelo SERVIDOR (NPC): posicao integrada no tique, idade zero.</summary>
		public bool ServidorMove;
		/// <summary>Quantos quadros por segundo o REMETENTE roda: 144 desalinha o envio do tique (27,8/34,7 ms).</summary>
		public double FpsDoRemetente = 60;
		/// <summary>A cada tantos inputs, um chega 30 ms atrasado: o tique ve zero inputs e o seguinte ve dois.</summary>
		public int EngasgoACada;
		public bool Voar;
		/// <summary>Um teleporte de 3000 px no meio do caminho (o contra-exemplo).</summary>
		public bool Teleporte;
		/// <summary>Instante (ms de servidor) em que o corpo VIRA pro norte sem parar. 0 = reto o tempo todo.</summary>
		public double CurvaEmMs;
		public int Semente = 7;
		public double Segundos = 9;
	}

	/// <summary>O que uma rodada do laboratorio devolve: os quadros medidos e a verdade sobre eles.</summary>
	private sealed class Rodada
	{
		public List<Quadro> Quadros = [];
		/// <summary>Latencia de desenho por quadro, em ms: `(x verdadeiro - x desenhado) / v`.</summary>
		public List<double> LatenciaMs = [];
		public double DistanciaReal;
		/// <summary>O quadro em que o snapshot do teleporte foi entregue e onde o corpo foi desenhado NELE.</summary>
		public int QuadroDoTeleporte = -1;
		public Vector2 DesenhadaNoTeleporte, DestinoDoTeleporte;
		public bool VoltouDepoisDoTeleporte;
		public List<Quadro> Subida = [];
	}

	// as constantes do mundo forjado
	private const double TickMs = Protocol.TickMs;
	private const double QuadroMs = 1000.0 / 60;
	/// <summary>Meu relogio = relogio do servidor + isto. Negativo e quebrado de proposito: nada pode depender de ele ser zero.</summary>
	private const double DeslocamentoLocal = -123456.75;
	/// <summary>Relogio do REMETENTE = relogio do servidor + isto.</summary>
	private const double DeslocamentoDoRemetente = 777.5;
	private const double SubidaMs = 8, DescidaMs = 12;
	private const double AquecimentoMs = 1500;

	private double _relogioLocalDeTeste;

	/// <summary>
	/// UMA RODADA DO LABORATORIO. O remetente manda inputs a 30 Hz pelo acumulador de quadros dele;
	/// o servidor forjado tica a 30 Hz noutra fase, traduz a hora de cada input pelo MESMO
	/// `DeslocamentoDeRelogio` de producao (o minimo em janela), carimba `PosMs` e escreve o
	/// snapshot com `servidorMs` e `idade`; o snapshot chega ao cliente com jitter; o cliente roda
	/// quadros de ~16,7 ms irregulares e entrega os pacotes na ordem de chegada (descartando os fora
	/// de ordem, como o canal sequenciado faz). Tudo o que o `RemotePlayer` ve e o que ele veria no jogo.
	/// </summary>
	private Rodada Rodar(RemotePlayer corpo, Cenario c)
	{
		var rnd = new Random(c.Semente);
		double Jitter() => (rnd.NextDouble() * 2 - 1) * c.JitterMs;
		var r = new Rodada();
		double v = c.PxPorSegundo;
		var origem = new Vector2(4000, 4000);
		double tTeleporte = c.Teleporte ? 5000 : double.MaxValue;
		// A VERDADE E UM CAMINHO: reto pra leste, e -- com `CurvaEmMs` -- dobrando pro norte sem parar,
		// na mesma velocidade. O norte e -Y no Godot.
		Vector2 Verdade(double tServidor)
		{
			double x, y = origem.Y;
			if (c.CurvaEmMs > 0 && tServidor >= c.CurvaEmMs)
			{
				x = origem.X + v * c.CurvaEmMs / 1000.0;
				y = origem.Y - v * (tServidor - c.CurvaEmMs) / 1000.0;
			}
			else x = origem.X + v * tServidor / 1000.0;
			if (tServidor >= tTeleporte) x += 3000;
			return new Vector2((float)x, (float)y);
		}
		Facing OlharVerdadeiro(double tServidor) => c.CurvaEmMs > 0 && tServidor >= c.CurvaEmMs ? Facing.North : Facing.East;
		// QUANTO DO CAMINHO um ponto ja percorreu (leste, depois norte) -- e o que a latencia compara.
		double Percorrido(Vector2 p) => (p.X - origem.X) + (origem.Y - p.Y);
		float AlturaVerdadeira(double tServidor)
			=> !c.Voar ? 0f : (float)Math.Min(Voo.AlturaMaxima, Voo.SubidaPorSegundo * Math.Max(0, tServidor - 2000) / 1000.0);

		// --- o remetente: um input por 33,3 ms do acumulador dele, com a hora DELE ---------------
		var inputs = new List<(int Seq, double Tempo, double Chegada, Vector2 Pos, Facing Olhar)>();
		{
			double quadroDoRemetente = 1000.0 / c.FpsDoRemetente, t = 3.0, acc = 0;
			int seq = 0;
			while (t < c.Segundos * 1000)
			{
				acc += quadroDoRemetente;
				t += quadroDoRemetente;
				if (acc >= TickMs)
				{
					acc -= TickMs;
					seq++;
					double atraso = SubidaMs + Jitter() + (c.EngasgoACada > 0 && seq % c.EngasgoACada == 0 ? 30 : 0);
					inputs.Add((seq, t + DeslocamentoDoRemetente, t + atraso, Verdade(t), OlharVerdadeiro(t)));
				}
			}
		}
		inputs.Sort((a, b) => a.Chegada.CompareTo(b.Chegada));

		// --- o servidor forjado: tica, traduz, carimba, escreve ------------------------------
		var relogioDoCliente = new DeslocamentoDeRelogio(maximo: false, janelaMs: 2000);
		var pacotes = new List<(int Tique, double ServidorMs, int Idade, Vector2 Pos, float Altura, double ChegadaLocal, bool Moving, Facing Olhar)>();
		{
			int proximoInput = 0, ultimoSeq = 0;
			Vector2 pos = Verdade(0);
			Facing olhar = Facing.East;
			double posMs = 0;
			bool vemDoCliente = false;
			for (int j = 0; j * TickMs + 17 < c.Segundos * 1000; j++)
			{
				double tTick = j * TickMs + 17 + (c.ServidorMove ? Jitter() * 0.3 : 0);
				if (c.ServidorMove)
				{
					// O NPC: integrado no tique com dt NOMINAL (como o `TickDosCorposSemDono`), escrito
					// pelo servidor -- idade zero.
					pos = Verdade(j * TickMs);
					olhar = OlharVerdadeiro(j * TickMs);
					vemDoCliente = false;
				}
				else
				{
					while (proximoInput < inputs.Count && inputs[proximoInput].Chegada <= tTick)
					{
						var i = inputs[proximoInput++];
						if (i.Seq <= ultimoSeq) continue;   // fora de ordem: o canal sequenciado descarta
						ultimoSeq = i.Seq;
						long chegada = (long)Math.Round(i.Chegada), tempo = (long)Math.Round(i.Tempo);
						relogioDoCliente.Amostrar(chegada - tempo, chegada);
						// A MESMA CONTA DO `GameServer.Input`: o valor suavizado, sem corte em zero.
						posMs = Math.Min(chegada + 100, tempo + (long)Math.Round(relogioDoCliente.Deslizar(chegada)));
						pos = i.Pos;
						olhar = i.Olhar;   // o olhar viaja com o input, como no `GameServer.Input`
						vemDoCliente = true;
					}
					if (c.Teleporte && j * TickMs + 17 >= tTeleporte && pos.X < origem.X + 3000)
					{
						// O SERVIDOR RECOLOCOU o corpo (Zanzoken, borda, embate): escrita dele, idade zero.
						pos = Verdade(tTick);
						vemDoCliente = false;
					}
				}
				long servidorMs = (long)Math.Round(tTick);
				int idade = vemDoCliente ? (int)Math.Clamp(servidorMs - Math.Round(posMs), sbyte.MinValue, EntityState.IdadeSaturada) : 0;
				float altura = Voo.DeByte(Voo.ParaByte(AlturaVerdadeira(j * TickMs)));
				pacotes.Add((j, servidorMs, idade, pos, altura, tTick + DescidaMs + Jitter() + DeslocamentoLocal, true, olhar));
			}
		}
		pacotes.Sort((a, b) => a.ChegadaLocal.CompareTo(b.ChegadaLocal));

		// --- o cliente: quadros irregulares, pacotes entregues na ordem de chegada ----------
		RemotePlayer.Relogio.Zerar();
		_relogioLocalDeTeste = DeslocamentoLocal;
		int proximoPacote = 0, ultimoTique = -1, quadro = 0;
		double fimLocal = c.Segundos * 1000 + DeslocamentoLocal;
		Vector2 anterior = default;
		bool teleporteVisto = false;
		while (_relogioLocalDeTeste < fimLocal)
		{
			double delta = QuadroMs + (rnd.NextDouble() * 2 - 1) * 3 + (quadro % 40 == 39 ? 12 : 0);
			_relogioLocalDeTeste += delta;
			bool entregouTeleporte = false;
			while (proximoPacote < pacotes.Count && pacotes[proximoPacote].ChegadaLocal <= _relogioLocalDeTeste)
			{
				var p = pacotes[proximoPacote++];
				if (p.Tique <= ultimoTique) continue;   // sequenciado
				ultimoTique = p.Tique;
				long s = RemotePlayer.Relogio.AoChegarSnapshot(unchecked((uint)p.ServidorMs));
				corpo.Receive(s, p.Idade, new Vec2(p.Pos.X, p.Pos.Y), p.Olhar, p.Moving, deitado: false,
							  Protocol.Pose.Normal, correndo: v > 200, altitude: p.Altura, voando: p.Altura > 0);
				if (c.Teleporte && !teleporteVisto && p.Pos.X >= origem.X + 3000) { teleporteVisto = true; entregouTeleporte = true; }
			}
			corpo._Process(delta / 1000.0);
			quadro++;

			var q = new Quadro(delta, _relogioLocalDeTeste, corpo.Position, corpo.PosicaoExataDeTeste, corpo.AlturaDeTeste,
							   corpo.MovendoDeTeste, corpo.OlharDeTeste, corpo.AtrasoDeTeste, corpo.InanicoesDeTeste);
			if (entregouTeleporte)
			{
				r.QuadroDoTeleporte = quadro;
				r.DesenhadaNoTeleporte = corpo.Position;
				r.DestinoDoTeleporte = pacotes.First(p => p.Pos.X >= origem.X + 3000).Pos;
			}
			else if (teleporteVisto && corpo.Position.X < origem.X + 3000 - 1) r.VoltouDepoisDoTeleporte = true;

			double tServidor = _relogioLocalDeTeste - DeslocamentoLocal;
			bool aquecido = _relogioLocalDeTeste - DeslocamentoLocal >= AquecimentoMs;
			if (aquecido && !c.Teleporte)
			{
				r.Quadros.Add(q);
				if (v > 0) r.LatenciaMs.Add((Percorrido(Verdade(tServidor)) - Percorrido(corpo.PosicaoExataDeTeste)) / v * 1000.0);
				// A JANELA DA SUBIDA E A DO DESENHO, nao a da verdade: o desenho esta ~100 ms no
				// passado, entao ele comeca a subir ~100 ms depois de a verdade comecar e chega ao
				// teto ~100 ms depois dela. Medir fora disso contaria como "congelado" o corpo ainda
				// no chao e o corpo ja no teto -- que e o certo, nao um defeito.
				if (c.Voar && AlturaVerdadeira(tServidor - 350) > 0 && AlturaVerdadeira(tServidor + 50) < Voo.AlturaMaxima)
					r.Subida.Add(q);
			}
			anterior = corpo.Position;
		}
		if (r.Quadros.Count > 1)
			r.DistanciaReal = v * (r.Quadros.Skip(1).Sum(q => q.DeltaMs)) / 1000.0;
		return r;
	}

	/// <summary>Um `RemotePlayer` de producao, com o `Visual` minimo que ele exige, fora do jogo.</summary>
	private RemotePlayer CorpoDeLaboratorio()
	{
		var r = new RemotePlayer { Name = "RemotoDeLaboratorio" };
		r.AddChild(new CharacterVisual { Name = "Visual" });
		AddChild(r);
		// O RELOGIO E MEU: o `_Process` e chamado a mao, com o delta que a rodada escolher.
		r.SetProcess(false);
		return r;
	}

	private void Laboratorio_()
	{
		RelogioDoServidor.RelogioDeTeste = () => _relogioLocalDeTeste;
		Nota($"laboratorio: criterios desvio/media < {DesvioMaximo}, salto = {FatorDeSalto}x a mediana, aquecimento {AquecimentoMs} ms; "
			 + "quadros de 16,7 ms +-3 com um engasgo de +12 ms a cada 40; jitter de fio +-10 ms; remetente a 60 e a 144 fps");

		var cenarios = new[]
		{
			new Cenario { Nome = "correr 352 px/s, jitter +-10", PxPorSegundo = 352 },
			new Cenario { Nome = "correr 352 px/s, remetente a 144 fps (degrau de fase)", PxPorSegundo = 352, FpsDoRemetente = 144, Semente = 11 },
			new Cenario { Nome = "correr 352 px/s, um input engasga a cada 7 (posicao repetida + dobrada)", PxPorSegundo = 352, EngasgoACada = 7, Semente = 3 },
			new Cenario { Nome = "supervoo 1760 px/s, jitter +-10", PxPorSegundo = 1760, Semente = 5 },
			new Cenario { Nome = "supervoo 1760 px/s, remetente a 144 fps", PxPorSegundo = 1760, FpsDoRemetente = 144, Semente = 13 },
			new Cenario { Nome = "supervoo 1760 px/s, um input engasga a cada 7", PxPorSegundo = 1760, EngasgoACada = 7, Semente = 17 },
			new Cenario { Nome = "NPC do servidor a 352 px/s (idade zero, tique com jitter)", PxPorSegundo = 352, ServidorMove = true, Semente = 19 },
			new Cenario { Nome = "decolar e correr a 352 px/s (altura na mesma linha do tempo)", PxPorSegundo = 352, Voar = true, Semente = 23 },
			new Cenario { Nome = "correr 352 px/s e VIRAR pro norte sem parar (olhar e passo no mesmo quadro)", PxPorSegundo = 352, CurvaEmMs = 5000, Semente = 31 },
			new Cenario { Nome = "supervoo 1760 px/s e VIRAR pro norte (o deslize virado do relato)", PxPorSegundo = 1760, CurvaEmMs = 5000, Semente = 37 },
		};

		foreach (Cenario c in cenarios)
		{
			RemotePlayer corpo = CorpoDeLaboratorio();
			Rodada r = Rodar(corpo, c);
			Medida m = Medir(r.Quadros);
			Julgar(c.Nome, m);

			double erro = r.DistanciaReal > 0 ? Math.Abs(m.Distancia - r.DistanciaReal) / r.DistanciaReal : 1;
			Checa($"{c.Nome}: distancia desenhada = distancia real (+-3%)", erro < 0.03,
				  $"desenhada {m.Distancia:0} px, real {r.DistanciaReal:0} px ({erro * 100:0.0}%)");

			double lat = r.LatenciaMs.Count > 0 ? r.LatenciaMs.Average() : double.NaN;
			double atraso = r.Quadros.Count > 0 ? r.Quadros[^1].AtrasoMs : double.NaN;
			int inanicoes = r.Quadros.Count > 0 ? r.Quadros[^1].Inanicoes : -1;
			// A LATENCIA DE DESENHO E O ATRASO MAIS A MENOR DESCIDA: o relogio estimado fica atras
			// do servidor pela latencia minima observada (ver `RelogioDoServidor`), e o carimbo do
			// input fica a frente da verdade pela menor subida. Descida 12 - 10 e subida 8 - 10 dao
			// ~ +2 ms e ~ -2 ms; a janela aceita e larga o bastante pro jitter dos dois.
			Checa($"{c.Nome}: latencia de desenho ~ atraso ({atraso:0} ms)", !double.IsNaN(lat) && lat > atraso - 25 && lat < atraso + 45,
				  $"latencia media {lat:0.0} ms, atraso em vigor {atraso:0.0} ms, inanicoes {inanicoes}");

			if (c.CurvaEmMs > 0) AVirada(c.Nome, r);

			if (c.Voar)
			{
				Medida sub = MedirAltura(r.Subida);
				// A ALTURA VIAJA NUM BYTE (2,5 px por degrau contra 6,4 px por tique de subida), e isso
				// deixa um vai-e-vem de ~20% entre amostras que o desenho a um quarto de escala nao
				// mostra. O criterio aqui e o que o olho ve: sem quadro parado e sem salto -- e o
				// desvio com a folga do byte.
				Checa($"{c.Nome}: a subida e continua (sem congelar, sem dobrar, desvio/media < 0,35)",
					  sub.Quadros >= 30 && sub.Congelamentos == 0 && sub.Saltos == 0 && sub.Media > 0 && sub.Desvio / sub.Media < 0.35,
					  Resumo(sub));
			}
			corpo.QueueFree();
		}

		// ============================ O CONTRA-EXEMPLO: SALTO DE SERVIDOR NAO SE INTERPOLA ============================
		{
			var c = new Cenario { Nome = "teleporte de 3000 px", PxPorSegundo = 352, Teleporte = true, Semente = 29 };
			RemotePlayer corpo = CorpoDeLaboratorio();
			Rodada r = Rodar(corpo, c);
			bool cravou = r.QuadroDoTeleporte >= 0 && r.DesenhadaNoTeleporte.DistanceTo(r.DestinoDoTeleporte) <= 1.0f;
			Checa("teleporte de 3000 px crava no MESMO quadro em que o snapshot chega", cravou,
				  r.QuadroDoTeleporte < 0 ? "o snapshot do teleporte nunca foi entregue"
					  : $"quadro {r.QuadroDoTeleporte}: desenhado em {r.DesenhadaNoTeleporte}, destino {r.DestinoDoTeleporte}");
			Checa("depois do teleporte o corpo NAO desliza de volta pro caminho antigo", cravou && !r.VoltouDepoisDoTeleporte,
				  r.VoltouDepoisDoTeleporte ? "voltou pra tras do destino em algum quadro" : "ficou do lado de la");
			corpo.QueueFree();
		}

		// ============================ O DEFEITO INJETADO ============================
		// ============================ DEFEITO INJETADO: A VIRADA NA CHEGADA ============================
		// O olhar aplicado no instante do pacote, como era: o corpo vira ~100 ms antes de o desenho
		// dobrar. A curva a 1760 px/s -- a do relato -- TEM que reprovar no juiz da virada.
		RemotePlayer.DefeitoViradaNaChegada = true;
		try
		{
			int antes = _falhas;
			RemotePlayer corpoV = CorpoDeLaboratorio();
			Rodada rv = Rodar(corpoV, new Cenario { Nome = "DEFEITO: supervoo 1760 px/s e VIRAR, olhar na chegada", PxPorSegundo = 1760, CurvaEmMs = 5000, Semente = 37 });
			bool reprovou = !AVirada("DEFEITO: supervoo 1760 px/s e VIRAR, olhar na chegada", rv, esperaPassar: false);
			corpoV.QueueFree();
			_falhas = antes;
			Checa("DEFEITO INJETADO (olhar aplicado na chegada) reprova: o corpo vira quadros ANTES de andar na direcao nova", reprovou,
				  reprovou ? "reprovou, como devia" : "PASSOU -- o juiz da virada nao enxerga o defeito");
		}
		finally { RemotePlayer.DefeitoViradaNaChegada = false; }

		// A MESMA alimentacao do cenario dos engasgos, carimbada pela hora de CHEGADA (o
		// comportamento anterior). Se ela passasse, esta bancada nao estaria enxergando o defeito
		// que veio consertar.
		{
			RemotePlayer.DefeitoCarimboDeChegada = true;
			try
			{
				int reprovados = 0;
				foreach (Cenario c in new[]
				{
					new Cenario { Nome = "DEFEITO: 352 px/s com engasgos, carimbo de chegada", PxPorSegundo = 352, EngasgoACada = 7, Semente = 3 },
					new Cenario { Nome = "DEFEITO: 1760 px/s a 144 fps, carimbo de chegada", PxPorSegundo = 1760, FpsDoRemetente = 144, Semente = 13 },
				})
				{
					RemotePlayer corpo = CorpoDeLaboratorio();
					Medida m = Medir(Rodar(corpo, c).Quadros);
					bool passou = Julgar(c.Nome, m, esperaPassar: false);
					if (!passou) reprovados++;
					Nota($"  (defeito) {c.Nome}: {Resumo(m)} -> {(passou ? "PASSOU (a bancada esta cega)" : "reprovou, como devia")}");
					corpo.QueueFree();
				}
				Checa("DEFEITO INJETADO (carimbo pela hora de chegada) reprova nos dois cenarios", reprovados == 2,
					  $"{reprovados} de 2 reprovaram");
			}
			finally { RemotePlayer.DefeitoCarimboDeChegada = false; }
		}

		RelogioDoServidor.RelogioDeTeste = null;
		RemotePlayer.Relogio.Zerar();
		Fechar();
	}

	/// <summary>A mesma estatistica, sobre a ALTURA desenhada (o eixo vertical do voo).</summary>
	private static Medida MedirAltura(IReadOnlyList<Quadro> quadros)
	{
		var m = new Medida();
		var vel = new List<double>();
		for (int i = 1; i < quadros.Count; i++)
		{
			double passo = Math.Abs(quadros[i].Altura - quadros[i - 1].Altura);
			double dt = quadros[i].DeltaMs / 1000.0;
			if (dt <= 0) continue;
			vel.Add(passo / dt);
			if (passo <= 0) m.Congelamentos++;
		}
		m.Quadros = vel.Count;
		if (vel.Count == 0) return m;
		var ordenada = vel.OrderBy(v => v).ToList();
		m.Mediana = ordenada[ordenada.Count / 2];
		m.Media = vel.Average();
		m.Desvio = Math.Sqrt(vel.Sum(v => (v - m.Media) * (v - m.Media)) / vel.Count);
		m.Saltos = vel.Count(v => v >= FatorDeSalto * m.Mediana);
		// A altura nao passa pela grade de desenho: a "exata" e a mesma.
		m.MediaExata = m.Media;
		m.DesvioExato = m.Desvio;
		m.SaltosExatos = m.Saltos;
		return m;
	}

	// =====================================================================
	// DOIS PROCESSOS
	// =====================================================================
	private bool _dentro;
	private double _relogio;
	private bool _viOOutro;
	private double _desdeQueVi;
	private string _fase = "";
	private bool _voou, _acabou;
	private readonly List<Quadro> _gravados = [];
	private Vector2? _ultimaPosDoSnapshot;
	private readonly List<double> _defasagens = [];

	public override void _Ready()
	{
		if (Laboratorio) return;   // roda no primeiro `_Process`: o `Visual` do corpo precisa da arvore de pe

		// SESSENTA QUADROS POR SEGUNDO, nos dois: headless roda o `_Process` o mais rapido que
		// puder, e um passo por quadro de meio pixel nao mede nada -- e o cliente do dono roda a 60.
		Engine.MaxFps = 60;

		// DEPOIS DE TODO MUNDO NO QUADRO: o B le a posicao que o `RemotePlayer._Process` acabou de
		// escrever NESTE quadro. Lendo antes, o passo seria o do quadro anterior contra o delta deste
		// -- e delta irregular viraria velocidade irregular sem nada de errado no desenho.
		ProcessPriority = 10_000;

		if (GameClient.Instance is not { } cli) return;
		cli.Joined += (id, z, spawn, nome) => { _dentro = true; _relogio = 0; GD.Print($"[fluidez-{Papel}] ENTREI: id {id} nome '{nome}'"); };
		cli.SnapshotReceived += Avistou;
		// O `Joined` JA PASSOU quando este no nasce (ver `RoboDeDoisCorpos`).
		if (cli.LocalId != 0) { _dentro = true; _relogio = 0; }
		GD.Print($"[fluidez-{Papel}] no ar (alvo '{Alvo}', duracao {Duracao}s)");
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.SnapshotReceived -= Avistou;
	}

	/// <summary>
	/// O A so comeca a andar quando VE o outro (o B precisa estar dentro pra medir); o B guarda a
	/// posicao crua do A.
	///
	/// "O OUTRO" E PELO NOME (`--fluidezalvo`), nos dois papeis: o berco e povoado, e o primeiro
	/// corpo alheio do snapshot e um NPC -- na primeira rodada o A arrancou ao ver o cidadao de id 1
	/// e terminou o roteiro antes de o B acabar de entrar. (E a mesma armadilha do `--socaralvo`.)
	/// </summary>
	private void Avistou(List<EntityState> estados)
	{
		if (GameClient.Instance is not { } cli) return;
		int alvo = Alvo.Length > 0 ? World.Instancia?.IdPeloNome(Alvo) ?? 0 : 0;
		foreach (EntityState e in estados)
		{
			if (e.Id == cli.LocalId) continue;
			if (Alvo.Length > 0 && e.Id != alvo) continue;
			if (!_viOOutro) { _viOOutro = true; _desdeQueVi = 0; GD.Print($"[fluidez-{Papel}] avistei o outro (id {e.Id}{(Alvo.Length > 0 ? $", '{Alvo}'" : "")})"); }
			if (Papel == "b")
			{
				_ultimaPosDoSnapshot = new Vector2(e.Pos.X, e.Pos.Y);
				// O FIO CRU, pra ler depois: a hora de chegada no meu relogio, o cabecalho, a idade e a
				// posicao. E o que separa "o desenho esta irregular" de "o que chega ja e irregular".
				_amostras.Add(((long)Math.Round(RelogioDoServidor.AgoraLocalMs()), cli.ServidorMsDoSnapshot, e.IdadeMs, e.Pos.X, e.Pos.Y, e.Moving));
			}
		}
	}

	/// <summary>As amostras cruas do alvo, como chegaram (ver <see cref="Avistou"/>). Vao pro CSV.</summary>
	private readonly List<(long LocalMs, long ServidorMs, int Idade, float X, float Y, bool Moving)> _amostras = [];

	/// <summary>
	/// OS DOIS CSVs DA RODADA (`user://fluidez-<rotulo>-quadros.csv` e `-amostras.csv`): quadro a
	/// quadro o que foi desenhado, e pacote a pacote o que chegou. O veredito e o placar; estes sao
	/// pra quando o placar diz "irregular" e alguem precisa saber ONDE -- no fio, no relogio ou no desenho.
	/// </summary>
	private void GravarCsv()
	{
		string rot = Rotulo.Length > 0 ? Rotulo : Papel;
		var ci = System.Globalization.CultureInfo.InvariantCulture;
		try
		{
			using (var f = Godot.FileAccess.Open($"user://fluidez-{rot}-quadros.csv", Godot.FileAccess.ModeFlags.Write))
			{
				f?.StoreString("delta_ms;local_ms;x;y;x_exata;y_exata;altura;movendo;olhar;atraso_ms;inanicoes\n");
				foreach (Quadro q in _gravados)
					f?.StoreString(string.Format(ci, "{0:0.000};{1:0.0};{2};{3};{4:0.000};{5:0.000};{6:0.0};{7};{8};{9:0.0};{10}\n",
												 q.DeltaMs, q.LocalMs, q.Desenhada.X, q.Desenhada.Y, q.Exata.X, q.Exata.Y, q.Altura,
												 q.Movendo ? 1 : 0, q.Olhar, q.AtrasoMs, q.Inanicoes));
			}
			using (var f = Godot.FileAccess.Open($"user://fluidez-{rot}-amostras.csv", Godot.FileAccess.ModeFlags.Write))
			{
				f?.StoreString("local_ms;servidor_ms;idade;x;y;movendo\n");
				foreach (var a in _amostras)
					f?.StoreString(string.Format(ci, "{0};{1};{2};{3};{4};{5}\n", a.LocalMs, a.ServidorMs, a.Idade, a.X, a.Y, a.Moving ? 1 : 0));
			}
		}
		catch (Exception ex) { GD.PushWarning($"[fluidez] nao gravei os CSVs: {ex.Message}"); }
	}

	private bool _rodouOLaboratorio;

	public override void _Process(double delta)
	{
		if (Laboratorio)
		{
			if (_rodouOLaboratorio) return;
			_rodouOLaboratorio = true;
			Laboratorio_();
			return;
		}
		if (!_dentro || _acabou) return;
		_relogio += delta;
		if (_viOOutro) _desdeQueVi += delta;

		if (Papel == "a") Andar(delta);
		else Olhar(delta);
	}

	private static readonly string[] Direcoes = ["move_right", "move_left", "move_up", "move_down"];

	private static void Segurar(string acao, bool sim)
	{
		if (sim) Godot.Input.ActionPress(acao);
		else Godot.Input.ActionRelease(acao);
	}

	/// <summary>
	/// O ROTEIRO DO A: pernas retas de ida e volta, ANDANDO, CORRENDO e VOANDO, segurando as acoes
	/// de verdade (`Input.ActionPress`, como o `RoboDeSoco`). Ida e volta pra nao sair do campo
	/// aberto do berco -- a 1760 px/s uma perna de 2,5 s ja sao 140 tiles.
	/// </summary>
	private void Andar(double delta)
	{
		if (!_viOOutro)
		{
			// SEM PLATEIA NAO HA O QUE MEDIR -- mas nao pra sempre: 40 s e o B nao entrou.
			if (_relogio > 40) { GD.Print("[fluidez-a] ninguem apareceu em 40 s; encerrando"); Terminar(); }
			return;
		}
		double t = _desdeQueVi - 3.0;   // tres segundos pro B se afastar do berco
		if (t < 0) return;

		// (inicio, fim, acao, correr) -- as pernas
		(double De, double Ate, string Acao, bool Correr)[] pernas =
		[
			(0.0, 1.5, "move_right", false), (1.5, 3.0, "move_left", false), (3.0, 4.5, "move_right", false), (4.5, 6.0, "move_left", false),
			(6.5, 8.0, "move_right", true), (8.0, 9.5, "move_left", true), (9.5, 11.0, "move_right", true), (11.0, 12.5, "move_left", true),
			(15.0, 17.5, "move_right", true), (17.5, 20.0, "move_left", true), (20.0, 22.5, "move_right", true), (22.5, 25.0, "move_left", true),
		];

		if (t >= 13.0 && !_voou)
		{
			_voou = true;
			GameClient.Instance?.SendHabilidade("voar");
			GD.Print("[fluidez-a] levantando voo");
		}

		string fase = t < 6.5 ? "andar" : t < 13 ? "correr" : t < 25 ? "voar" : "fim";
		if (fase != _fase) { _fase = fase; GD.Print($"[fluidez-a] fase: {fase}"); }

		bool algum = false;
		foreach (var p in pernas)
		{
			bool ativa = t >= p.De && t < p.Ate;
			if (ativa)
			{
				algum = true;
				foreach (string d in Direcoes) Segurar(d, d == p.Acao);
				Segurar("run", p.Correr);
			}
		}
		if (!algum)
		{
			foreach (string d in Direcoes) Godot.Input.ActionRelease(d);
			Godot.Input.ActionRelease("run");
		}

		if (t >= 28.0) Terminar();
	}

	private void Terminar()
	{
		if (_acabou) return;
		_acabou = true;
		// SEIS SEGUNDOS: o B fecha a conta aos 33 s de roteiro (ver `Duracao`) e precisa do host de
		// pe ate la. Quem mata o que sobrar do B e o `.bat`, pela linha de comando.
		GD.Print("[fluidez-a] roteiro concluido; fechando em 6 s");
		if (GetTree() is { } arv) arv.CreateTimer(6.0).Timeout += () => GetTree()?.Quit();
	}

	/// <summary>
	/// O B: sai do berco (senao o A nasce dentro dele e o primeiro passo bate no corpo) e grava, por
	/// quadro, onde o `RemotePlayer` do A foi desenhado -- o MESMO node que o jogo desenha, achado
	/// pela MESMA busca (`World.CorpoDeTeste`).
	/// </summary>
	private void Olhar(double delta)
	{
		Segurar("move_down", _relogio < 1.5);
		if (!_viOOutro) return;

		int id = World.Instancia?.IdPeloNome(Alvo) ?? 0;
		if (id != 0 && World.Instancia?.CorpoDeTeste(id) is RemotePlayer r)
		{
			_gravados.Add(new Quadro(delta * 1000.0, RelogioDoServidor.AgoraLocalMs(), r.Position, r.PosicaoExataDeTeste,
									 r.AlturaDeTeste, r.MovendoDeTeste, r.OlharDeTeste, r.AtrasoDeTeste, r.InanicoesDeTeste));
			// A DEFASAGEM: quanto o desenho esta atras da posicao crua do ultimo snapshot, em px. Nao e
			// criterio (o B nao sabe a verdade); e o numero que diz, em ms, quanto se esta olhando pro
			// passado -- dividido pela velocidade, la no veredito.
			if (_ultimaPosDoSnapshot is { } crua && r.MovendoDeTeste) _defasagens.Add(crua.DistanceTo(r.PosicaoExataDeTeste));
		}

		if (_desdeQueVi >= Duracao) JulgarADupla();
	}

	/// <summary>
	/// A VELOCIDADE DESENHADA CONTRA A DO FIO, quadro a quadro, numa perna reta.
	///
	/// As amostras cruas do alvo (`_amostras`) que chegaram durante a perna viram trechos
	/// `(s_i, s_i+1, v_i)` ao longo do eixo da perna; cada quadro desenhado cai num trecho pela sua
	/// posicao e a razao `v_desenhada / v_fio` e o que se mede. A faixa de tolerancia usa os vizinhos
	/// (trecho anterior e seguinte): na fronteira entre um trecho lento e um rapido o quadro pode
	/// pertencer a qualquer um dos dois.
	///
	/// Trechos com o fio PARADO (dx zero) nao contam: o corpo desenhado parado ali esta certo.
	/// </summary>
	private (int Quadros, double RazaoMedia, double Desvio, int Saltos, int Congelamentos, double VelDoFio) ContraOFio(List<Quadro> perna)
	{
		if (perna.Count < 2) return (0, 0, 0, 0, 0, 0);
		double de = perna[0].LocalMs - 400, ate = perna[^1].LocalMs + 100;
		var fio = _amostras.Where(a => a.LocalMs >= de && a.LocalMs <= ate && a.Moving)
						   .Select(a => (T: (double)(a.ServidorMs - a.Idade), Pos: new Vector2(a.X, a.Y)))
						   .OrderBy(a => a.T).ToList();
		if (fio.Count < 3) return (0, 0, 0, 0, 0, 0);

		Vector2 eixo = (fio[^1].Pos - fio[0].Pos);
		if (eixo.Length() < 1f) return (0, 0, 0, 0, 0, 0);
		eixo = eixo.Normalized();

		// os trechos, sem as amostras repetidas (mesma hora)
		var trechos = new List<(double S0, double S1, double V)>();
		for (int i = 1; i < fio.Count; i++)
		{
			double dt = fio[i].T - fio[i - 1].T;
			if (dt <= 0) continue;
			double s0 = fio[i - 1].Pos.Dot(eixo), s1 = fio[i].Pos.Dot(eixo);
			trechos.Add((s0, s1, Math.Abs(s1 - s0) / dt * 1000.0));
		}
		if (trechos.Count < 2) return (0, 0, 0, 0, 0, 0);

		var razoes = new List<double>();
		int saltos = 0, congelamentos = 0;
		double somaV = 0; int nV = 0;
		for (int k = 1; k < perna.Count; k++)
		{
			double passo = (perna[k].Desenhada - perna[k - 1].Desenhada).Length();
			double dt = perna[k].DeltaMs / 1000.0;
			if (dt <= 0) continue;
			double s = perna[k].Exata.Dot(eixo);
			int i = trechos.FindIndex(t => s >= Math.Min(t.S0, t.S1) - 0.01 && s <= Math.Max(t.S0, t.S1) + 0.01);
			if (i < 0) continue;   // fora do que o fio cobre (o comeco ou o fim da perna)
			double vMin = double.MaxValue, vMax = 0;
			for (int j = Math.Max(0, i - 1); j <= Math.Min(trechos.Count - 1, i + 1); j++)
			{
				vMin = Math.Min(vMin, trechos[j].V);
				vMax = Math.Max(vMax, trechos[j].V);
			}
			if (vMax <= 0) continue;   // o fio esta parado aqui
			double v = passo / dt;
			somaV += trechos[i].V; nV++;
			if (passo <= 0 && vMin > 0) congelamentos++;
			if (v >= FatorDeSalto * vMax) saltos++;
			// a razao contra o trecho mais proximo em velocidade (a fronteira e ambigua)
			double vRef = v < vMin ? vMin : v > vMax ? vMax : v;
			razoes.Add(vRef > 0 ? v / vRef : 0);
		}
		if (razoes.Count == 0) return (0, 0, 0, 0, 0, 0);
		double media = razoes.Average();
		double desvio = Math.Sqrt(razoes.Sum(r => (r - media) * (r - media)) / razoes.Count);
		return (razoes.Count, media, media > 0 ? desvio / media : 1, saltos, congelamentos, nV > 0 ? somaV / nV : 0);
	}

	/// <summary>
	/// O VEREDITO DOS DOIS PROCESSOS. A gravacao e cortada em PERNAS: trechos em que o servidor diz
	/// que o corpo anda e ele olha pro mesmo lado. Os primeiros <see cref="QuadrosDeArranque"/>
	/// quadros de cada perna nao contam -- o flag chega ~100 ms antes de o desenho (que esta no
	/// passado) sair do lugar, e o arranque parado e o certo, nao um congelamento.
	/// </summary>
	private void JulgarADupla()
	{
		_acabou = true;
		foreach (string d in Direcoes) Godot.Input.ActionRelease(d);
		GravarCsv();

		Nota($"dois processos ({Rotulo}): {_gravados.Count} quadros gravados do corpo de '{Alvo}'");
		var pernas = new List<List<Quadro>>();
		List<Quadro>? atual = null;
		for (int i = 0; i < _gravados.Count; i++)
		{
			Quadro q = _gravados[i];
			bool continua = q.Movendo && atual != null && atual.Count > 0 && atual[^1].Olhar == q.Olhar;
			if (!continua)
			{
				if (atual != null && atual.Count > QuadrosDeArranque + 30) pernas.Add(atual);
				atual = q.Movendo ? [] : null;
			}
			atual?.Add(q);
		}
		if (atual != null && atual.Count > QuadrosDeArranque + 30) pernas.Add(atual);

		Nota($"  {pernas.Count} perna(s) com quadros suficientes (>= {QuadrosDeArranque + 30})");
		Checa("o A andou o bastante pra medir (pelo menos 4 pernas)", pernas.Count >= 4, $"{pernas.Count} perna(s)");

		// ============================ O JUIZ E O FIO, NAO A CONSTANCIA ============================
		// O laboratorio sabe a verdade; aqui a verdade mais proxima e o que CHEGOU: a serie de
		// amostras cruas do alvo, com a hora de cada uma. O corpo real muda de velocidade por conta
		// propria (a primeira rodada mostrou o A alternando 97 e 213 px/s no ar, em rajadas de
		// 330 ms, com a tecla de correr segurada o tempo todo -- coisa do voo, nao do desenho), e
		// cobrar velocidade constante reprovaria o desenho pelo que o jogo fez. O que se cobra e que
		// o desenho REPRODUZA o fio: em cada quadro, a velocidade desenhada dividida pela velocidade
		// do trecho do fio em que o corpo esta tem que ficar perto de 1 -- sem quadro em 2x e sem
		// quadro parado onde o fio anda.
		// ==========================================================================================
		int n = 0;
		double piorDesvio = 0; int saltos = 0, congelamentos = 0, quadros = 0;
		var velocidades = new List<double>();
		foreach (List<Quadro> perna in pernas)
		{
			n++;
			(int q, double razaoMedia, double desvio, int s, int c, double vel) = ContraOFio(perna.Skip(QuadrosDeArranque).ToList());
			piorDesvio = Math.Max(piorDesvio, q > 0 ? desvio : 1);
			saltos += s; congelamentos += c; quadros += q;
			velocidades.Add(vel);
			Nota($"  perna {n} ({perna[0].Olhar}, ~{vel:0} px/s no fio): {q} quadros | desenho/fio razao media {razaoMedia:0.000}, desvio/media {desvio:0.000} | "
				 + $"saltos >= {FatorDeSalto}x o fio: {s} | congelamentos com o fio andando: {c}");
		}
		Checa($"o desenho reproduz o fio em toda perna (pior desvio/media da razao < {DesvioMaximo})", pernas.Count > 0 && piorDesvio < DesvioMaximo, $"pior {piorDesvio:0.000} em {quadros} quadros");
		Checa($"nenhum quadro com passo >= {FatorDeSalto}x o que o fio anda ali", pernas.Count > 0 && saltos == 0, $"{saltos} salto(s)");
		Checa("nenhum congelamento onde o fio anda", pernas.Count > 0 && congelamentos == 0, $"{congelamentos} quadro(s) parado(s)");

		double atraso = _gravados.Count > 0 ? _gravados[^1].AtrasoMs : double.NaN;
		int inanicoes = _gravados.Count > 0 ? _gravados[^1].Inanicoes : -1;
		double defasagem = _defasagens.Count > 0 ? _defasagens.Average() : double.NaN;
		double velMedia = velocidades.Count > 0 ? velocidades.Average() : 0;
		Nota($"  atraso em vigor no fim: {atraso:0.0} ms | inanicoes: {inanicoes} | defasagem media desenho x snapshot cru: {defasagem:0.0} px"
			 + (velMedia > 0 ? $" (~{defasagem / velMedia * 1000:0} ms a {velMedia:0} px/s)" : ""));
		Checa("o atraso de desenho ficou perto da base (< 150 ms): a rede local nao provocou inanicao em serie",
			  !double.IsNaN(atraso) && atraso < 150, $"{atraso:0.0} ms, {inanicoes} inanicao(oes)");

		bool voou = _gravados.Any(q => q.Altura > 0);
		Checa("o A foi visto VOANDO (a fase do voo aconteceu)", voou, voou ? $"altura maxima vista {_gravados.Max(q => q.Altura):0} px" : "altura sempre zero");

		Fechar();
	}
}
