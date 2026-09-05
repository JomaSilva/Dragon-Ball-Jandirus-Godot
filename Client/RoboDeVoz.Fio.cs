using Concentus;
using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ `--diagvoz`, AS FAMILIAS DO PICOTE ============================
/// O dono: *"o audio do voice chat ta ruim, fica picotando e nao ta fluido"*. As cinco secoes do
/// `RoboDeVoz.cs` medem codec, parede, rampa, tecla e o desligado -- nenhuma delas mede o que ha
/// entre o pacote e o alto-falante, que e onde o picote morava. Estas tres medem:
///
///  6. O FIO COM JITTER -- pacotes Opus DE VERDADE (codificados pelo codificador de producao) chegando
///     com jitter, rajada, buraco, perda, um par fora de ordem, uma duplicata e um atrasado, na
///     `FilaDeJitter` de producao, com um gerador SIMULADO que consome como o do Godot. Mede zeros
///     inseridos, quadros tocados, ordem, cobertura da perda. E o DEFEITO INJETADO: a fila desligada
///     (`FilaDeJitter.SemFilaDeTeste`) com a MESMA alimentacao tem que produzir zeros.
///  7. O EMISSOR -- o portao com histerese e hangover, o anel que impede o engasgo de virar rajada e o
///     reamostrador com anti-alias, em amostras e em contagem de `Mandar`.
///  8. A TORNEIRA -- o balde novo de 15: uma rajada de 10 passa inteira, uma de 30 perde 15.
///
/// ============================ POR QUE O GERADOR E SIMULADO ============================
/// O gerador do Godot no `--headless` consome em pedacos de ~100 ms (medido: driver Dummy, mix a
/// 44,1 kHz) -- uma bancada em cima dele mediria o Dummy, nao a fila. O simulado consome exatamente
/// `delta x 16` amostras por passo, com o delta irregular que o `_Process` de verdade tem
/// (16,7 ms +- 8), e conta os ZEROS que teria tocado: e a unica medida honesta de picote, porque e o
/// que sairia no alto-falante. A fila nao sabe qual dos dois esta do outro lado -- ela so recebe a
/// ocupacao --, entao o codigo medido e o de producao, inteiro.
/// ======================================================================================
/// </summary>
public partial class RoboDeVoz
{
	// =====================================================================
	// O PLACAR DA INJECAO
	// =====================================================================
	/// <summary>
	/// SEPARADO DO OUTRO, de proposito (a mesma disciplina da `--vozdupla`): uma checagem verde e uma
	/// checagem que nao viu defeito; uma que fica vermelha com o defeito na frente e uma que SABE
	/// OLHAR. Somar as duas esconderia a diferenca.
	/// </summary>
	private int _injecoesQuePegaram;
	private readonly List<string> _injecoesQuePassaramBatido = [];

	private void Injetar(string oque, bool ficouVermelha, string detalhe = "")
	{
		if (ficouVermelha) { _injecoesQuePegaram++; GD.Print($"[diagvoz]   pegou (injecao) {oque}   {detalhe}"); return; }
		_injecoesQuePassaramBatido.Add(oque);
		GD.PrintErr($"[diagvoz]   PASSOU BATIDO (injecao) {oque}   {detalhe}");
	}

	// =====================================================================
	// 1b. O FEC EXISTE
	// =====================================================================
	/// <summary>
	/// A COPIA REDUNDANTE E ESCRITA NOS PACOTES -- medida, e nao afirmada.
	///
	/// Tons alternados por quadro (500 Hz nos pares, 1 kHz nos impares). Perde-se o quadro k+1 e
	/// decodifica-se o pacote k+2 com `decode_fec`: se a copia existe, o que sai tem o tom de k+1; se
	/// nao existe, o Opus faz PLC calado e o que sai e a extrapolacao de k -- o tom ERRADO. Foi assim
	/// que se descobriu que a 16 kbit/s a copia nunca era escrita (ver `VozLocal.BitsPorSegundo`).
	///
	/// A injecao e o proprio 16 kbit/s de antes: o mesmo teste, com o codificador de ontem, tem que
	/// dar zero.
	/// </summary>
	private void OFecExisteDeVerdade()
	{
		(int certos, double bytes) Medir(IOpusEncoder enc)
		{
			IOpusDecoder dec = OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeSaida, 1);
			var buf = new byte[VozLocal.MaxBytesDeQuadro];
			var pcm = new short[VozLocal.AmostrasPorQuadro];
			var volta = new short[VozLocal.AmostrasPorQuadroDeSaida];   // decodifica em 48 kHz, como o jogo
			var pacotes = new List<byte[]>();
			double soma = 0;
			for (int k = 0; k < 40; k++)
			{
				Array.Clear(pcm);
				Somar(pcm, k % 2 == 0 ? HzPar : HzImpar, 0.4f);
				int n = enc.Encode(pcm, VozLocal.AmostrasPorQuadro, buf, buf.Length);
				pacotes.Add(buf[..n]);
				if (k >= 10) soma += n;
			}
			int certos = 0;
			for (int k = 10; k + 2 < 40; k += 3)
			{
				dec.Decode(pacotes[k], volta, VozLocal.AmostrasPorQuadroDeSaida, decode_fec: false);        // toca k
				dec.Decode(pacotes[k + 2], volta, VozLocal.AmostrasPorQuadroDeSaida, decode_fec: true);     // k+1 perdido: a copia que k+2 carrega
				double ep = Energia(volta, volta.Length, HzPar, VozLocal.TaxaDeSaida), ei = Energia(volta, volta.Length, HzImpar, VozLocal.TaxaDeSaida);
				if ((k + 1) % 2 == 0 ? ep > ei : ei > ep) certos++;
				dec.Decode(pacotes[k + 2], volta, VozLocal.AmostrasPorQuadroDeSaida, decode_fec: false);    // e k+2 normal
			}
			return (certos, soma / 30);
		}

		(int certos, double bytes) = Medir(Microfone.CriarCodificador());
		GD.Print($"[diagvoz]   ...  FEC a {VozLocal.BitsPorSegundo / 1000} kbit/s: tom certo em {certos}/10, {bytes:0.0} B/quadro com a copia");
		Conferir(certos >= 8, "o pacote seguinte CARREGA a copia do perdido (o FEC existe nesta taxa)", $"{certos}/10");

		IOpusEncoder deOntem = Microfone.CriarCodificador();
		deOntem.Bitrate = 16000;
		(int certosDeOntem, double bytesDeOntem) = Medir(deOntem);
		Injetar("a 16 kbit/s (o codificador de ontem) o Opus NAO escreve a copia: `decode_fec` vira PLC do quadro errado",
			certosDeOntem <= 2, $"{certosDeOntem}/10 certos, {bytesDeOntem:0.0} B/quadro");
	}

	// =====================================================================
	// 6. O FIO COM JITTER
	// =====================================================================
	/// <summary>O gerador do Godot, em quatro linhas: uma fila que o motor esvazia em tempo real.</summary>
	private sealed class GeradorSimulado
	{
		public const int Capacidade = 16384;   // o que 0,3 s a 48 kHz (14.400) vira depois do arredondamento do motor
		public int Ocupacao;
		public long Zeros;
		public bool Tocando;
		private double _resto;

		/// <summary>O motor pediu `ms` de audio. O que nao houver na fila sai como zero -- e e isso que se ouve.</summary>
		public void Consumir(double ms, bool falaNoAr)
		{
			_resto += ms * VozLocal.TaxaDeSaida / 1000.0;   // o gerador consome na taxa de SAIDA
			int amostras = (int)_resto;
			_resto -= amostras;
			int lidas = Math.Min(Ocupacao, amostras);
			Ocupacao -= lidas;
			if (Tocando && falaNoAr) Zeros += amostras - lidas;
		}
	}

	private sealed class Pacote
	{
		public ushort Seq;
		public byte[] Dados = [];
		public double ChegaMs;
		public int Ordem;
	}

	private sealed class QuadroTocado
	{
		public FilaDeJitter.Tipo Tipo;
		public ushort Seq;
		public double Rms, EPar, EImpar;
		public double LatenciaMs;
	}

	private sealed class Rodada
	{
		public readonly GeradorSimulado Gerador = new();
		public readonly List<QuadroTocado> Tocados = [];
		public FilaDeJitter Fila = null!;
	}

	private const int QuadrosNoFio = 150;
	private const float HzPar = 500f, HzImpar = 1000f;

	/// <summary>
	/// A ONDA DE CADA QUADRO DEPENDE DA PARIDADE DA SEQUENCIA: 500 Hz nos pares, 1 kHz nos impares.
	/// E assim que a ORDEM vira uma medida: um quadro tocado na vaga errada tem o tom errado dominando,
	/// e a bancada nao precisa confiar no numero que a fila diz ter tocado -- ela ouve. Os dois tons
	/// fecham ciclo inteiro em 20 ms (10 e 20 ciclos), entao nao ha degrau na emenda.
	/// </summary>
	private static List<Pacote> CodificarOFio()
	{
		IOpusEncoder enc = Microfone.CriarCodificador();
		var buf = new byte[VozLocal.MaxBytesDeQuadro];
		var pcm = new short[VozLocal.AmostrasPorQuadro];
		var pacotes = new List<Pacote>();
		for (int k = 0; k < QuadrosNoFio; k++)
		{
			Array.Clear(pcm);
			Somar(pcm, k % 2 == 0 ? HzPar : HzImpar, 0.4f);
			int n = enc.Encode(pcm, VozLocal.AmostrasPorQuadro, buf, buf.Length);
			pacotes.Add(new Pacote { Seq = (ushort)k, Dados = buf[..n] });
		}
		return pacotes;
	}

	/// <summary>
	/// O ROTEIRO DE CHEGADA -- determinista (semente fixa), pra dois rodadas verem o mesmo fio:
	///   * jitter de 0 a 12 ms em todo pacote;
	///   * rajadas: a cada 10, dois chegam juntos; a cada 25, tres chegam juntos;
	///   * buracos de 50 e 55 ms (tres pacotes segurados e soltos de uma vez) em 50 e 100;
	///   * 5% de perda (todo `k % 20 == 7`);
	///   * um par fora de ordem (61 antes de 60);
	///   * uma duplicata (90, duas vezes);
	///   * um atrasado alem de qualquer janela (120, 400 ms depois da hora -- o alvo maximo e 200).
	/// </summary>
	private static List<Pacote> Roteiro(List<Pacote> fio)
	{
		var rng = new Random(7);
		var chegadas = new List<Pacote>();
		int ordem = 0;
		foreach (Pacote p in fio)
		{
			int k = p.Seq;
			if (k % 20 == 7) continue;                       // perdido

			double t = 100 + 20 * k + rng.Next(0, 12);
			if (k % 10 == 3) t = 100 + 20 * (k + 1) + 4;      // junta com o seguinte (rajada de 2)
			if (k % 25 is 11 or 12) t = 100 + 20 * (k + (13 - k % 25)) + 5;   // rajada de 3
			if (k is 50 or 51 or 52) t = 100 + 20 * 52 + 50;  // buraco de 50 ms
			if (k is 100 or 101 or 102) t = 100 + 20 * 102 + 55;
			if (k == 61) t = 100 + 20 * 60 + 3;               // fora de ordem: o 61 vem antes...
			if (k == 60) t = 100 + 20 * 61 + 2;               // ...e o 60 vem depois
			if (k == 120) t = 100 + 20 * 120 + 400;           // atrasado alem da janela

			chegadas.Add(new Pacote { Seq = p.Seq, Dados = p.Dados, ChegaMs = t, Ordem = ordem++ });
			if (k == 90) chegadas.Add(new Pacote { Seq = p.Seq, Dados = p.Dados, ChegaMs = t + 8, Ordem = ordem++ });
		}
		chegadas.Sort((a, b) => a.ChegaMs != b.ChegaMs ? a.ChegaMs.CompareTo(b.ChegaMs) : a.Ordem.CompareTo(b.Ordem));
		return chegadas;
	}

	/// <summary>
	/// RODA O FIO INTEIRO na fila de producao, com o gerador simulado e o `_Process` irregular.
	/// O `delta` anda 16,7 ms +- 8 (semente fixa); a cada passo o gerador consome, os pacotes que
	/// "chegaram" entram, e a fila e puxada -- na mesma ordem do `VozOuvida._Process`.
	/// </summary>
	private Rodada RodarOFio(List<Pacote> chegadas)
	{
		var r = new Rodada();
		double ocupacaoAoEntregar = 0, chegadaDoAtual = 0, agora = 0;
		var chegadaPorSeq = new Dictionary<ushort, double>();
		foreach (Pacote p in chegadas) chegadaPorSeq.TryAdd(p.Seq, p.ChegaMs);

		IOpusDecoder dec = OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeSaida, 1);
		r.Fila = new FilaDeJitter(dec, (pcm, tipo, seq, _, _) =>
		{
			r.Gerador.Ocupacao += VozLocal.AmostrasPorQuadroDeSaida;
			if (tipo == FilaDeJitter.Tipo.Silencio) return;
			r.Gerador.Tocando = true;
			double soma = 0;
			for (int i = 0; i < VozLocal.AmostrasPorQuadroDeSaida; i++) { double v = pcm[i] / (double)short.MaxValue; soma += v * v; }
			chegadaPorSeq.TryGetValue(seq, out chegadaDoAtual);
			r.Tocados.Add(new QuadroTocado
			{
				Tipo = tipo, Seq = seq,
				Rms = Math.Sqrt(soma / VozLocal.AmostrasPorQuadroDeSaida),
				EPar = Energia(pcm, VozLocal.AmostrasPorQuadroDeSaida, HzPar, VozLocal.TaxaDeSaida),
				EImpar = Energia(pcm, VozLocal.AmostrasPorQuadroDeSaida, HzImpar, VozLocal.TaxaDeSaida),
				// boca-ouvido do lado de ca: quanto esperou no buffer + quanto ha na frente dele
				LatenciaMs = tipo == FilaDeJitter.Tipo.Normal
					? (agora - chegadaDoAtual) + ocupacaoAoEntregar * 1000.0 / VozLocal.TaxaDeSaida : 0,
			});
		});

		var rng = new Random(11);
		int proximo = 0;
		double ultimaChegada = double.NegativeInfinity;
		while (agora < chegadas[^1].ChegaMs + 600)
		{
			double delta = 16.7 + (rng.NextDouble() * 16 - 8);
			agora += delta;
			// FALA NO AR = chegou pacote nos ultimos 120 ms. E o unico criterio honesto pros zeros: na
			// pausa entre duas frases o gerador vazio e silencio legitimo, e num buraco de 50 ms ele e
			// picote. (O emissor manda hangover de 260 ms, entao dentro de uma frase nunca ha 120 ms
			// sem pacote.)
			r.Gerador.Consumir(delta, agora - ultimaChegada <= 120);

			while (proximo < chegadas.Count && chegadas[proximo].ChegaMs <= agora)
			{
				Pacote p = chegadas[proximo++];
				ultimaChegada = p.ChegaMs;
				r.Fila.Receber(p.Seq, p.Dados, 0, false, (long)agora);
			}

			ocupacaoAoEntregar = r.Gerador.Ocupacao;
			r.Fila.Puxar(r.Gerador.Ocupacao, GeradorSimulado.Capacidade - r.Gerador.Ocupacao, (long)agora);
		}
		return r;
	}

	private void OFioComJitter()
	{
		GD.Print("[diagvoz] --- 6. o fio com jitter: a fila de producao, um gerador simulado ---");
		List<Pacote> chegadas = Roteiro(CodificarOFio());
		int perdidos = QuadrosNoFio - chegadas.Select(p => p.Seq).Distinct().Count();

		Rodada r = RodarOFio(chegadas);
		FilaDeJitter f = r.Fila;
		List<QuadroTocado> voz = r.Tocados;
		int normais = voz.Count(q => q.Tipo == FilaDeJitter.Tipo.Normal);
		int fec = voz.Count(q => q.Tipo == FilaDeJitter.Tipo.Fec);
		int plc = voz.Count(q => q.Tipo == FilaDeJitter.Tipo.Plc);
		int esticados = voz.Count(q => q.Tipo == FilaDeJitter.Tipo.Esticado);
		double latencia = voz.Where(q => q.Tipo == FilaDeJitter.Tipo.Normal).Select(q => q.LatenciaMs).DefaultIfEmpty(0).Average();

		GD.Print($"[diagvoz]   ...  fio: {QuadrosNoFio} enviados, {chegadas.Count} chegadas ({perdidos} perdidos, 1 duplicata, 1 par fora de ordem, 1 atrasado)");
		GD.Print($"[diagvoz]   ...  tocados: {normais} normais + {fec} FEC + {plc} PLC + {esticados} esticados | soltos {f.Soltos} | "
			   + $"atrasados {f.Atrasados} | duplicados {f.Duplicados} | reinicios {f.Reinicios} | alvo final {f.Alvo} quadros");
		GD.Print($"[diagvoz]   ...  zeros no alto-falante com fala no ar: {r.Gerador.Zeros} amostras | underruns contados pela fila: {f.Underruns} | "
			   + $"latencia media no ouvinte (buffer + fila do gerador): {latencia:0} ms");
		// (i) ZERO underruns depois da pre-carga -- a medida e do gerador simulado, nao da fila.
		Conferir(r.Gerador.Zeros == 0,
			"(i) depois da pre-carga NENHUM zero foi inserido com fala no ar (o gerador nunca secou)",
			$"{r.Gerador.Zeros} amostras de zero");
		Conferir(voz.Count > 0 && voz[0].Tipo == FilaDeJitter.Tipo.Normal,
			"(i) e o primeiro quadro de voz tocado e um quadro de verdade, com a pre-carga na frente dele");

		// (ii) cada quadro enviado ocupou EXATAMENTE uma vaga na saida: tocado, remendado ou solto de
		// proposito. Um a mais seria duplicata tocada; um a menos, truncamento ou vaga pulada calada.
		Conferir(normais + fec + plc + f.Soltos == QuadrosNoFio,
			$"(ii) amostras tocadas = amostras enviadas: normais + FEC + PLC + soltos = {QuadrosNoFio}",
			$"{normais} + {fec} + {plc} + {f.Soltos} = {normais + fec + plc + f.Soltos}");
		Conferir(f.Soltos <= 2, "(ii) e a deriva soltou no maximo 2 quadros em 3 s de fio cheio de rajada", $"{f.Soltos}");
		Conferir(f.Duplicados == 1, "a duplicata foi descartada (e so ela)", $"{f.Duplicados}");
		Conferir(f.Atrasados == 1, "o pacote 400 ms atrasado foi descartado (a vaga dele ja tinha sido remendada, e o alvo subiu)", $"{f.Atrasados}");

		// (iii) a ORDEM, ouvida: o i-esimo quadro que saiu tem que ser o de sequencia i -- e o tom que
		// domina nele tem que ser o da paridade de i. A primeira metade le o que a fila DIZ que tocou; a
		// segunda ouve o que saiu, e nao depende da fila dizer a verdade.
		(int posicaoErrada, int tomErrado) = ForaDaOrdem(voz);
		Conferir(posicaoErrada == 0, "(iii) as vagas tocadas sao consecutivas, do 0 ao 149, sem pular nem repetir", $"{posicaoErrada} fora da posicao");
		Conferir(tomErrado == 0,
			"(iii) o par fora de ordem (61 antes de 60) tocou na ORDEM CERTA: em cada posicao domina o tom daquela vaga (ouvido)",
			$"{tomErrado} quadro(s) decodificado(s) com o tom da outra paridade");

		// (iv) a perda coberta: cada vaga perdida saiu com FEC ou PLC, e com energia.
		double rmsNormal = Mediana(voz.Where(q => q.Tipo == FilaDeJitter.Tipo.Normal).Select(q => q.Rms));
		var perdidas = Enumerable.Range(0, QuadrosNoFio).Where(k => k % 20 == 7).Select(k => (ushort)k).ToList();
		int cobertas = perdidas.Count(k => voz.Any(q => q.Seq == k && q.Tipo is FilaDeJitter.Tipo.Fec or FilaDeJitter.Tipo.Plc && q.Rms > rmsNormal * 0.25));
		Conferir(cobertas == perdidas.Count,
			"(iv) toda vaga perdida foi coberta por FEC ou PLC com energia (nao caiu a zero no buraco)",
			$"{cobertas} de {perdidas.Count} (rms normal {rmsNormal:0.000})");
		int fecComOTomCerto = voz.Count(q => q.Tipo == FilaDeJitter.Tipo.Fec && (q.Seq % 2 == 0 ? q.EPar > q.EImpar : q.EImpar > q.EPar));
		Conferir(fec >= perdidas.Count && fecComOTomCerto * 3 >= fec * 2,
			"(iv) e pelo FEC de verdade: a copia que o seguinte carrega tem o tom do PERDIDO, nao do anterior",
			$"FEC {fec} ({fecComOTomCerto} com o tom certo), PLC {plc}");

		// a adaptacao: os buracos de 50 ms passaram do alvo inicial de 60 ms menos a reserva, entao
		// houve esticada -- e o alvo subiu por isso, sem passar do teto.
		Conferir(f.Alvo > FilaDeJitter.AlvoInicial && f.Alvo <= FilaDeJitter.AlvoMaximo,
			"o alvo subiu depois dos buracos e ficou dentro do teto (adaptativo)", $"{f.Alvo} quadros");
		Conferir(esticados <= QuadrosNoFio / 10, "e as esticadas foram poucas (o alvo maior absorveu o resto)", $"{esticados}");

		// ============================ O DEFEITO INJETADO ============================
		// A fila desligada e o `VozOuvida` de antes: decodifica e empurra na chegada. A MESMA
		// alimentacao tem que secar o gerador -- senao a linha verde de (i) nao prova nada.
		FilaDeJitter.SemFilaDeTeste = true;
		try
		{
			Rodada d = RodarOFio(chegadas);
			var seqsSemFila = d.Tocados.Select(q => q.Seq).ToList();
			bool invertido = seqsSemFila.Zip(seqsSemFila.Skip(1)).Any(p => (short)(ushort)(p.Second - p.First) < 0);
			(int posicaoErradaSemFila, int tomErradoSemFila) = ForaDaOrdem(d.Tocados);
			GD.Print($"[diagvoz]   ...  SEM a fila: zeros {d.Gerador.Zeros} | tocados {d.Tocados.Count} | fora da posicao {posicaoErradaSemFila} | tom errado {tomErradoSemFila}");
			Injetar("com a fila de jitter DESLIGADA a mesma alimentacao insere zeros (a linha (i) fica vermelha)",
				d.Gerador.Zeros > 0, $"{d.Gerador.Zeros} amostras de zero");
			Injetar("...e o par fora de ordem toca invertido, e a duplicata toca duas vezes (a linha (iii) fica vermelha)",
				invertido && tomErradoSemFila > 0, $"invertido={invertido}, {tomErradoSemFila} com o tom errado");
			Injetar("...e os perdidos nao sao cobertos (a linha (ii) fica vermelha)",
				d.Tocados.Count(q => q.Tipo == FilaDeJitter.Tipo.Normal) < QuadrosNoFio, $"{d.Tocados.Count} tocados de {QuadrosNoFio}");
		}
		finally { FilaDeJitter.SemFilaDeTeste = false; }

		AFilaNumaFalaRetomada();
	}

	/// <summary>
	/// A ORDEM PELA POSICAO: o i-esimo quadro de voz que saiu (esticadas nao contam, elas nao ocupam
	/// vaga) deveria ser a vaga `primeiro + i`. Devolve quantos a fila DISSE estar em outra vaga e
	/// quantos quadros DECODIFICADOS DE VERDADE (os normais) tem, ouvindo, o tom da outra paridade.
	///
	/// A COPIA FEC FICA DE FORA DO OUVIDO: ela e uma reconstrucao de taxa mais baixa, e em super-wide (24
	/// kHz codificado, 48 ouvido) ela devolve o tom certo em 9 de 10 (medido pela familia 2, que e quem a
	/// cobra). Contar a copia aqui fazia a ORDEM parecer errada quando o que falhou foi a copia -- um em dez.
	/// </summary>
	private static (int PosicaoErrada, int TomErrado) ForaDaOrdem(List<QuadroTocado> voz)
	{
		int posicaoErrada = 0, tomErrado = 0, i = 0;
		ushort primeiro = voz.Count > 0 ? voz[0].Seq : (ushort)0;
		foreach (QuadroTocado q in voz)
		{
			if (q.Tipo == FilaDeJitter.Tipo.Esticado) continue;
			ushort esperada = (ushort)(primeiro + i);
			if (q.Seq != esperada) posicaoErrada++;
			if (q.Tipo == FilaDeJitter.Tipo.Normal
				&& (esperada % 2 == 0 ? q.EPar < q.EImpar : q.EImpar < q.EPar)) tomErrado++;
			i++;
		}
		return (posicaoErrada, tomErrado);
	}

	private static double Mediana(IEnumerable<double> xs)
	{
		var v = xs.OrderBy(x => x).ToList();
		return v.Count == 0 ? 0 : v[v.Count / 2];
	}

	/// <summary>
	/// DUAS FRASES COM PAUSA NO MEIO: a segunda tambem comeca com pre-carga -- e continua a sequencia.
	///
	/// E o caso de toda conversa (a pessoa solta a tecla e aperta de novo), e era onde o desmonte de
	/// 1,5 s recriava tudo. Aqui a fila e a mesma, o gerador esvaziou na pausa, e a segunda frase tem
	/// que sair sem zero nenhum. E o fim de fala nao pode ter contado como engasgo: o alvo fica onde
	/// estava.
	/// </summary>
	private void AFilaNumaFalaRetomada()
	{
		List<Pacote> fio = CodificarOFio();
		var chegadas = new List<Pacote>();
		int ordem = 0;
		for (int k = 0; k < 40; k++) chegadas.Add(new Pacote { Seq = fio[k].Seq, Dados = fio[k].Dados, ChegaMs = 100 + 20 * k, Ordem = ordem++ });
		for (int k = 40; k < 80; k++) chegadas.Add(new Pacote { Seq = fio[k].Seq, Dados = fio[k].Dados, ChegaMs = 100 + 20 * k + 1500, Ordem = ordem++ });

		Rodada r = RodarOFio(chegadas);
		int normais = r.Tocados.Count(q => q.Tipo == FilaDeJitter.Tipo.Normal);
		GD.Print($"[diagvoz]   ...  duas frases com 1,5 s de pausa: {normais} normais, zeros {r.Gerador.Zeros}, reinicios {r.Fila.Reinicios}, alvo {r.Fila.Alvo}");
		Conferir(r.Gerador.Zeros == 0, "a segunda frase (mesma fila, gerador vazio) tambem saiu sem zero nenhum", $"{r.Gerador.Zeros}");
		Conferir(normais == 80, "e as duas frases tocaram inteiras", $"{normais}");
		Conferir(r.Fila.Alvo == FilaDeJitter.AlvoInicial,
			"o fim da primeira frase NAO contou como engasgo (o alvo ficou no inicial)", $"{r.Fila.Alvo}");
	}

	// =====================================================================
	// 7. O EMISSOR
	// =====================================================================
	private static void Ruido(short[] q, float amp, Random rng)
	{
		for (int i = 0; i < q.Length; i++) q[i] = (short)((rng.NextDouble() * 2 - 1) * amp * short.MaxValue);
	}

	private static short[] QuadroAlto()
	{
		var q = new short[VozLocal.AmostrasPorQuadro];
		Somar(q, 500f, 0.3f);
		return q;
	}

	private static short[] QuadroBaixo(float amp, Random rng)
	{
		var q = new short[VozLocal.AmostrasPorQuadro];
		Ruido(q, amp, rng);
		return q;
	}

	private void OEmissorNaoPicota()
	{
		GD.Print("[diagvoz] --- 7. o emissor: portao com memoria, engasgo sem rajada, anti-alias ---");
		OPortaoTemMemoria();
		OEngasgoNaoViraRajada();
		OReamostradorNaoDobra();
	}

	/// <summary>
	/// O PORTAO, medido em quadros que passam. Dez altos e quarenta de ar de quarto: saem 10 + 13 (o
	/// hangover), nem 10 (o portao antigo, que cortava a pausa) nem 50 (um portao sem limiar). E a
	/// histerese: o que fica entre a metade e o limiar mantem o portao aberto, mas nao o abre.
	/// </summary>
	private void OPortaoTemMemoria()
	{
		var rng = new Random(3);
		const float limiar = 0.02f;
		int Passam(Microfone.PortaoDeVoz p, IEnumerable<short[]> quadros) => quadros.Count(q => p.Passa(q, limiar));

		IEnumerable<short[]> FalaEPausa() =>
			Enumerable.Range(0, 10).Select(_ => QuadroAlto()).Concat(Enumerable.Range(0, 40).Select(_ => QuadroBaixo(0.004f, rng)));

		int comHangover = Passam(new Microfone.PortaoDeVoz(), FalaEPausa());
		Conferir(comHangover == 10 + Microfone.PortaoDeVoz.QuadrosDeHangover,
			$"10 quadros de fala + 40 de ar: saem 10 + {Microfone.PortaoDeVoz.QuadrosDeHangover} (o hangover de 260 ms), e depois cala",
			$"{comHangover}");
		Injetar("com o hangover ZERADO (o portao antigo) a pausa e cortada na hora: saem so os 10",
			Passam(new Microfone.PortaoDeVoz(0), FalaEPausa()) == 10);

		Conferir(Passam(new Microfone.PortaoDeVoz(), Enumerable.Range(0, 20).Select(_ => QuadroBaixo(0.004f, rng))) == 0,
			"so ar de quarto, do inicio: NENHUM quadro sai (o hangover nao abre portao)");

		// A HISTERESE: 0,015 esta entre a metade (0,01) e o limiar (0,02).
		var meio = new short[VozLocal.AmostrasPorQuadro];
		Somar(meio, 500f, 0.015f);
		var portao = new Microfone.PortaoDeVoz();
		Conferir(Passam(portao, Enumerable.Repeat(meio, 10)) == 0, "abaixo do limiar mas acima da metade, de portao FECHADO: nao abre");
		Conferir(portao.Passa(QuadroAlto(), limiar) && Passam(portao, Enumerable.Repeat(meio, 30)) == 30,
			"depois de um quadro alto, os mesmos 0,015 MANTEM o portao aberto por 30 quadros (histerese, nao hangover)");
		portao.Fechar();
		Conferir(Passam(portao, Enumerable.Repeat(meio, 5)) == 0, "e soltar a tecla (`Fechar`) apaga a memoria");
	}

	/// <summary>
	/// PELO CAMINHO DE PRODUCAO: a onda entra pela `FonteDeTeste`, passa pelo anel, pelo portao, pelo
	/// codificador, e o espiao conta o que `Mandar` mandou. Uma fonte que devolve 25 quadros numa
	/// visita so (o jogo travou meio segundo) tem que virar 5 pacotes -- e 20 descartes contados.
	/// </summary>
	private void OEngasgoNaoViraRajada()
	{
		var rng = new Random(5);
		int mandados = 0;
		void Contar(int bytes, int amostras) => mandados++;
		Microfone.Espiao += Contar;

		// A FONTE DEVOLVE ATE `cota` QUADROS POR VISITA -- e a cota e o que distingue "um quadro por
		// quadro de tela" (jogo) de "25 de uma vez" (engasgo). Sem ela o dreno esvaziaria a fila
		// inteira na primeira visita, e o teste da pausa mediria o anel em vez do portao.
		var fila = new Queue<short[]>();
		int cota = 0;
		Microfone.FonteDeTeste = q =>
		{
			if (fila.Count == 0 || cota <= 0) return false;
			cota--;
			Array.Copy(fila.Dequeue(), q, q.Length);
			return true;
		};

		var mic = new Microfone { Name = "MicrofoneDoEngasgo" };
		AddChild(mic);
		void Visita(int quadros) { cota = quadros; mic.DrenarDeTeste(); }
		try
		{
			// A PAUSA DENTRO DA FRASE, PELO CAMINHO INTEIRO: um quadro por visita, como em jogo.
			for (int i = 0; i < 10; i++) fila.Enqueue(QuadroAlto());
			for (int i = 0; i < 40; i++) fila.Enqueue(QuadroBaixo(0.004f, rng));
			while (fila.Count > 0) Visita(1);
			Conferir(mandados == 10 + Microfone.PortaoDeVoz.QuadrosDeHangover,
				$"pelo `Mandar` de producao, 10 de fala + 40 de ar mandam {10 + Microfone.PortaoDeVoz.QuadrosDeHangover} pacotes",
				$"{mandados}");

			// O ENGASGO: 25 quadros numa visita so.
			mandados = 0;
			for (int i = 0; i < 25; i++) fila.Enqueue(QuadroAlto());
			Visita(25);
			Conferir(mandados == Microfone.QuadrosProntos.Teto,
				$"25 quadros acumulados numa visita (engasgo de 500 ms) viram {Microfone.QuadrosProntos.Teto} pacotes, nao 25",
				$"{mandados} mandados");
			Conferir(mic.QuadrosDescartadosPorEngasgo == 25 - Microfone.QuadrosProntos.Teto,
				"e os 20 mais VELHOS foram os descartados (contados)", $"{mic.QuadrosDescartadosPorEngasgo}");

			// O CONTRA-EXEMPLO: tres quadros numa visita e a rajada legitima de um quadro de tela longo.
			mandados = 0;
			for (int i = 0; i < 3; i++) fila.Enqueue(QuadroAlto());
			Visita(3);
			Conferir(mandados == 3 && mic.QuadrosDescartadosPorEngasgo == 25 - Microfone.QuadrosProntos.Teto,
				"tres quadros numa visita (quadro de tela longo) saem os tres, sem descarte", $"{mandados}");
		}
		finally
		{
			// METODO NOMEADO E `-=`; os dois espioes sao estaticos e sobreviveriam a este no.
			Microfone.Espiao -= Contar;
			Microfone.FonteDeTeste = null;
			mic.QueueFree();
		}

		// E O ANEL SOZINHO, com o teto de 25 no lugar do de 5: a mesma medida passa a ver 25. E o que
		// separa "o anel corta" de "a fonte devolveu menos".
		var largo = new Microfone.QuadrosProntos(25);
		for (int i = 0; i < 25; i++) largo.Por(QuadroAlto());
		int saiu = 0;
		largo.Despachar(_ => saiu++);
		Injetar("com o anel alargado pra 25 (o dreno antigo) os 25 saem de uma vez", saiu == 25 && largo.Descartados == 0, $"{saiu}");
	}

	/// <summary>
	/// O ANTI-ALIAS, medido: 1 kHz + 12 kHz a 48 kHz. A 16 kHz o 12 kHz nao existe -- ele DOBRA pra
	/// 4 kHz, em cima das consoantes. Depois do reamostrador de producao, a energia em 4 kHz tem que
	/// ser uma fracao pequena da de 1 kHz; pegando uma amostra a cada tres (o reamostrador antigo,
	/// escrito aqui como contra-exemplo) ela e IGUAL.
	/// </summary>
	private void OReamostradorNaoDobra()
	{
		const double taxa = 48000;
		var cru = new Vector2[(int)(taxa * 0.25)];
		for (int i = 0; i < cru.Length; i++)
		{
			float v = 0.3f * MathF.Sin(2 * MathF.PI * 1000f * i / (float)taxa)
					+ 0.3f * MathF.Sin(2 * MathF.PI * 20000f * i / (float)taxa);
			cru[i] = new Vector2(v, v);
		}

		short[] ultimo = [];
		int fechados = new Microfone.Reamostrador(taxa).Alimentar(cru, q => ultimo = (short[])q.Clone());
		Conferir(fechados == 12, "250 ms a 48 kHz fecham exatamente 12 quadros de 20 ms (o passo fracionario nao deriva)", $"{fechados}");

		double e1k = Energia(ultimo, ultimo.Length, 1000f);
		double e4k = Energia(ultimo, ultimo.Length, 4000f);
		double razao = e4k / Math.Max(e1k, 1e-9);
		GD.Print($"[diagvoz]   ...  reamostrado: E(1 kHz)={e1k:0.0000} E(4 kHz, o 20 kHz dobrado)={e4k:0.0000}  razao={razao:0.000} ({20 * Math.Log10(Math.Max(razao, 1e-9)):0.0} dB)");
		Conferir(e1k > 0.1, "o 1 kHz atravessa o reamostrador (a voz nao e comida junto com o alias)", $"{e1k:0.000}");
		Conferir(razao < 0.12, "o 20 kHz dobrado em 4 kHz (48 -> 24 kHz) cai mais de 18 dB (anti-alias de verdade)", $"{razao:0.000}");

		// O CONTRA-EXEMPLO: uma amostra a cada tres, sem filtro -- o que o `Drenar` fazia.
		var pontual = new short[VozLocal.AmostrasPorQuadro];
		int ini = cru.Length - 2 * VozLocal.AmostrasPorQuadro;
		for (int i = 0; i < pontual.Length; i++)
		{
			Vector2 s = cru[ini + 2 * i];
			pontual[i] = (short)Mathf.Clamp((int)((s.X + s.Y) * 0.5f * short.MaxValue), short.MinValue, short.MaxValue);
		}
		double razaoPontual = Energia(pontual, pontual.Length, 4000f) / Math.Max(Energia(pontual, pontual.Length, 1000f), 1e-9);
		Injetar("pegando uma amostra a cada duas (o reamostrador antigo) o 20 kHz dobra INTEIRO pra 4 kHz",
			razaoPontual > 0.7, $"razao {razaoPontual:0.000}");

		// E A PLACA DE 44,1 kHz: o passo e 2,75625 e mesmo assim 250 ms fecham 12 quadros.
		var cru441 = new Vector2[(int)(44100 * 0.25)];
		for (int i = 0; i < cru441.Length; i++)
		{
			float v = 0.3f * MathF.Sin(2 * MathF.PI * 1000f * i / 44100f);
			cru441[i] = new Vector2(v, v);
		}
		int fechados441 = new Microfone.Reamostrador(44100).Alimentar(cru441, q => ultimo = (short[])q.Clone());
		Conferir(fechados441 == 12 && Energia(ultimo, ultimo.Length, 1000f) > 0.1,
			"a 44,1 kHz (passo 1,8375) tambem: 12 quadros em 250 ms, e o 1 kHz continua 1 kHz", $"{fechados441} quadros");
	}

	/// <summary>
	/// 9 -- O GANHO AUTOMATICO. O dono: "o audio do voice chat naturalmente ta muito baixo". A regra e pura
	/// (`Microfone.ControleDeGanho`), entao mede-se com senos: a voz baixa sobe ate o alvo, a alta nao e
	/// tocada, o grito depois do sussurro nao serra, o ar nao vira chiado -- e o ganho FIXO de ontem
	/// (teto 1x) deixa a voz baixa exatamente onde estava.
	/// </summary>
	private void OGanhoAutomaticoLevantaAVozBaixa()
	{
		GD.Print("[diagvoz] --- 9. o ganho automatico: voz baixa sobe, voz alta nao estoura, ar nao vira chiado ---");
		static double Rms(short[] q) { double s = 0; foreach (short a in q) { double v = a / (double)short.MaxValue; s += v * v; } return Math.Sqrt(s / q.Length); }
		static short[] Seno(float amp) { var q = new short[VozLocal.AmostrasPorQuadro]; Somar(q, 500f, amp); return q; }

		var agc = new Microfone.ControleDeGanho();
		double rmsBaixo = Rms(Seno(0.03f));
		short[] ultimo = Seno(0.03f);
		for (int i = 0; i < 60; i++) { ultimo = Seno(0.03f); agc.Aplicar(ultimo); }
		double depois = Rms(ultimo);
		Conferir(depois > rmsBaixo * 3 && depois >= Microfone.ControleDeGanho.RmsAlvo * 0.8,
			$"uma voz a -33 dBFS (RMS {rmsBaixo:0.000}) sobe em 60 quadros (1,2 s) pra perto do alvo {Microfone.ControleDeGanho.RmsAlvo}",
			$"RMS {depois:0.000}, ganho {agc.Ganho:0.00}x");
		Conferir(agc.Ganho <= Microfone.ControleDeGanho.GanhoMax + 1e-3, "...e o ganho respeita o teto de 8x", $"{agc.Ganho:0.00}x");

		var alto = new Microfone.ControleDeGanho();
		short[] forte = Seno(0.9f);
		double rmsForte = Rms(forte);
		alto.Aplicar(forte);
		Conferir(Math.Abs(alto.Ganho - 1f) < 1e-3 && Math.Abs(Rms(forte) - rmsForte) < 1e-6,
			"uma voz ja alta (-1 dBFS) passa INTACTA: ganho 1x, nem um bit mexido", $"ganho {alto.Ganho:0.00}x");

		short[] grito = Seno(0.9f);
		agc.Aplicar(grito);
		int pico = grito.Max(a => Math.Abs((int)a));
		Conferir(pico < short.MaxValue && agc.Ganho < 4f,
			"um grito logo depois do sussurro: o ganho cai na hora (metade do caminho num quadro) e o pico nao serra no teto",
			$"pico {pico}, ganho {agc.Ganho:0.00}x");

		var ar = new Microfone.ControleDeGanho();
		var rng = new Random(7);
		for (int i = 0; i < 100; i++) ar.Aplicar(QuadroBaixo(0.001f, rng));
		Conferir(Math.Abs(ar.Ganho - 1f) < 1e-3, "cem quadros de AR (abaixo do piso) nao movem o ganho: silencio nao vira chiado", $"{ar.Ganho:0.00}x");

		var preso = new Microfone.ControleDeGanho(ganhoMax: 1f);
		short[] q = Seno(0.03f);
		for (int i = 0; i < 60; i++) { q = Seno(0.03f); preso.Aplicar(q); }
		Injetar("com o teto em 1x (o ganho FIXO de ontem) a voz baixa continua exatamente baixa", Rms(q) < rmsBaixo * 1.01, $"RMS {Rms(q):0.000}");

		Conferir(VozLocal.TaxaDeAmostragem == 24000 && VozLocal.AmostrasPorQuadro == 480 && VozLocal.BitsPorSegundo >= 28000,
			"CONTRATO: a voz sai em super-wide (24 kHz, 480 amostras por quadro) com bitrate que enche a banda (>= 28 kbit/s)",
			$"{VozLocal.TaxaDeAmostragem} Hz, {VozLocal.AmostrasPorQuadro}, {VozLocal.BitsPorSegundo} bit/s");
	}

	// =====================================================================
	// 8. A TORNEIRA
	// =====================================================================
	/// <summary>
	/// O BALDE NOVO, no objeto puro (o caminho de verdade e medido na `--vozteste`, pelo contador do
	/// servidor). Dez de uma vez e a rajada honesta de dois drenos juntos: passa inteira. Trinta e um
	/// cliente modificado: exatamente quinze morrem.
	/// </summary>
	private void ATorneiraAceitaARajadaHonesta()
	{
		GD.Print("[diagvoz] --- 8. a torneira: rajada de 10 passa, rajada de 30 perde 15 ---");
		var honesta = new VozLocal.Torneira();
		int passaram = Enumerable.Range(0, 10).Count(_ => honesta.Cabe(1000, 40));
		Conferir(passaram == 10 && honesta.Recusados == 0,
			"10 quadros no mesmo instante passam INTEIROS (era 5, e 5 picotava voz honesta)", $"{passaram} passaram, {honesta.Recusados} recusados");

		var desonesta = new VozLocal.Torneira();
		passaram = Enumerable.Range(0, 30).Count(_ => desonesta.Cabe(1000, 40));
		Conferir(passaram == (int)VozLocal.RajadaDeQuadros && desonesta.Recusados == 30 - (int)VozLocal.RajadaDeQuadros,
			$"30 no mesmo instante: {VozLocal.RajadaDeQuadros} passam e {30 - VozLocal.RajadaDeQuadros} sao recusados",
			$"{passaram} passaram, {desonesta.Recusados} recusados");
		Conferir(VozLocal.RajadaDeQuadros * VozLocal.MsPorQuadro == 300, "o balde vale 300 ms", $"{VozLocal.RajadaDeQuadros * VozLocal.MsPorQuadro} ms");
	}
}
