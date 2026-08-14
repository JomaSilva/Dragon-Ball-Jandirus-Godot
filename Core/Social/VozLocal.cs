namespace Jandirus.Core.Social;

/// <summary>
/// ============================ A VOZ LOCAL: AS REGRAS, LONGE DE QUALQUER MOTOR ============================
/// *"cria um sistema de chat de VOZ LOCAL (voip) q ao apertar V vc consegue falar com pessoas PERTO de
/// vc, e com EFEITO DE DISTANCIA caso se afaste, e tb se tiver ATRAS DE PAREDE da uma ABAFADA"* -- o dono.
///
/// ============================ A DECISAO QUE MANDA NO SISTEMA INTEIRO ============================
/// **O CORTE DE ALCANCE E DO SERVIDOR, E NAO DO BOTAO DE VOLUME.** O caminho preguicoso e mandar a voz
/// pra zona inteira e deixar o cliente abaixar conforme a distancia. Isso SOA igual e **e outra coisa**:
/// quem le os pacotes ouve a sala inteira, e nao ha volume que conserte um byte que ja chegou.
///
/// Este port ja errou exatamente isso duas vezes, e as duas custaram um sistema inteiro pra desfazer:
/// o **sigilo do BP** (a API do corte foi escrita e ficou ORFA -- o numero vazava por sete lugares) e a
/// **barra de vida**, que so parou de mentir quando o numero parou de sair do servidor. Com voz e pior,
/// porque nao e um numero: e a voz de uma pessoa de verdade.
///
/// Entao a lei e uma frase: **quem esta longe NAO RECEBE BYTE NENHUM** -- nao "recebe baixinho". A
/// distancia e a parede so modelam o que chega em quem **ja tinha direito de ouvir**.
/// ============================================================================================
///
/// ============================ O QUE ESTE ARQUIVO **NAO** DECIDE ============================
/// Ele nao sabe o que e uma zona, o que e uma parede nem o que e um `AudioServer`. O corte de verdade
/// mora no `GameServer.Voz.cs` (que tem a lista da zona e o bitset do que cega) e a reproducao mora no
/// `Client/VozOuvida.cs` (que tem o alto-falante). Aqui ficam so os numeros que as DUAS pontas
/// precisam concordar -- e concordar sobre o tamanho de um quadro de audio nao e opcional: um lado
/// codificando 20 ms e o outro esperando 10 vira ruido branco, nao vira "um bug".
/// ======================================================================================
/// </summary>
public static class VozLocal
{
	// =====================================================================
	// O QUADRO
	// =====================================================================
	/// <summary>
	/// 16 kHz mono -- voz, e nao musica.
	///
	/// A faixa util da fala humana termina perto de 8 kHz, e o teorema de Nyquist entrega isso com
	/// 16 kHz. Dobrar pra 32 dobraria a banda pra ganhar o chiado do "s" -- e a voz vai concorrer com
	/// o snapshot do jogo no mesmo cano.
	/// </summary>
	public const int TaxaDeAmostragem = 16000;

	/// <summary>
	/// 20 ms por quadro. E o quadro padrao do Opus e nao por acaso: menos que isso e o cabecalho
	/// (opcode + id + sequencia + UDP/IP = 33 B) passa a pesar mais que o audio; mais que isso e a
	/// latencia da conversa comeca a se ouvir.
	///
	/// MEDIDO: o `AudioEffectCapture` do Godot entrega em lotes de ate 1024 quadros a 48 kHz --
	/// 21,3 ms. Ou seja, 20 ms e tambem o tamanho natural do que o motor devolve; pedir 10 ms
	/// obrigaria a fatiar lote, e pedir 40 obrigaria a juntar dois.
	/// </summary>
	public const int MsPorQuadro = 20;

	/// <summary>320 amostras. E o que o codificador recebe e o que o decodificador devolve.</summary>
	public const int AmostrasPorQuadro = TaxaDeAmostragem * MsPorQuadro / 1000;

	/// <summary>50 quadros por segundo. E o teto honesto de quem fala -- ver <see cref="Torneira"/>.</summary>
	public const int QuadrosPorSegundo = 1000 / MsPorQuadro;

	/// <summary>
	/// 16 kbit/s, VBR. **MEDIDO com Opus (Concentus, C# puro), quadro de 20 ms:**
	///
	///   | compl | bitrate | B/quadro |   KB/s | codificar | % de um quadro |
	///   |-------|---------|----------|--------|-----------|----------------|
	///   |   0   |  16000  |   39,8   |  1,94  |   135 us  |     0,68%      |
	///   | **3** |**16000**| **39,9** |**1,95**| **102 us**|   **0,51%**    |
	///   |   3   |  12000  |   29,8   |  1,46  |   107 us  |     0,53%      |
	///   |   3   |  24000  |   59,9   |  2,92  |   105 us  |     0,52%      |
	///   |   8   |  16000  |   39,8   |  1,94  |   284 us  |     1,42%      |
	///
	/// Contra os vizinhos: PCM cru 16 kHz = 640 B/quadro (**31,2 KB/s**), ADPCM 4 bits = 160 B
	/// (**7,8 KB/s**), Opus = 40 B (**1,95 KB/s**). Dezesseis vezes mais barato que o cru e quatro
	/// vezes mais barato que o ADPCM.
	///
	/// **E o servidor nao paga codec nenhum**: ele repassa o payload OPACO e so decide quem recebe.
	/// Por isso a economia vale onde a banda multiplica -- no leque de ouvintes --, e e ali que os
	/// 4x do ADPCM teriam doido.
	/// </summary>
	public const int BitsPorSegundo = 16000;

	/// <summary>
	/// Complexidade 3. Cinco e oito custam 70% e 180% mais CPU por **zero** byte de ganho
	/// (39,9 -> 39,8 B na tabela acima). Pagar 180% a mais por um decimo de byte seria escolher pelo
	/// jogador, com a bateria dele.
	/// </summary>
	public const int Complexidade = 3;

	/// <summary>
	/// TETO DE TAMANHO DE UM QUADRO NO FIO. Quadro maior que isto e DESCARTADO sem dó.
	///
	/// O Opus a 16 kbit/s da ~40 B; 120 e o triplo, folga pra um pico de VBR num "sss" e ainda assim
	/// longe de virar cano. **Sem este teto um cliente modificado vira torneira**: ele mandaria 4 KB
	/// por "quadro" e o servidor os multiplicaria pelo numero de ouvintes. O teto e do SERVIDOR e nao
	/// do cliente, porque a unica coisa que um cliente modificado nao faz e se limitar.
	/// </summary>
	public const int MaxBytesDeQuadro = 120;

	// =====================================================================
	// O TETO POR FALANTE
	// =====================================================================
	/// <summary>
	/// QUANTOS QUADROS DE CREDITO ALGUEM PODE ACUMULAR -- a folga da torneira.
	///
	/// Cinco quadros (100 ms). Nao e generosidade: a rede entrega em rajada, e o cliente que perdeu
	/// um quadro de agendamento manda dois juntos no seguinte. Sem folga, jitter normal viraria voz
	/// picotada; com folga demais, um cliente modificado guardaria credito pra despejar de uma vez.
	/// </summary>
	public const double RajadaDeQuadros = 5;

	/// <summary>
	/// DEPOIS DE QUANTO SILENCIO ALGUEM DEIXA DE "ESTAR FALANDO", em ms.
	///
	/// 250 ms = 12 quadros e meio. Responde DUAS perguntas de uma vez, e de proposito:
	///   * o sinal sobre a cabeca apaga (ver `SinalDeVoz`);
	///   * o falante sai da conta do <see cref="MaxFalantesPorOuvinte"/>, liberando a vaga.
	/// Um valor apertado demais faria o sinal piscar entre as palavras de uma frase.
	/// </summary>
	public const long MsAteCalar = 250;

	/// <summary>
	/// QUANTAS VOZES UM OUVINTE RECEBE AO MESMO TEMPO -- as <b>4 mais proximas</b>.
	///
	/// ============================ ISTO E O TETO DE BANDA DO SERVIDOR, E NAO CONFORTO ============================
	/// Sem ele o leque e M falantes x N ouvintes. Com ele e 4 x N, e isso muda a ORDEM da conta:
	///
	///   | falantes x ouvintes | sem teto  | com teto |
	///   |---------------------|-----------|----------|
	///   |        3 x 15       | 173 KB/s  | 173 KB/s |
	///   |       20 x 20       | 1,54 MB/s | 308 KB/s |
	///
	/// (a 3,85 KB/s por fluxo: 49 B de quadro + 28 B de UDP/IP, 50x por segundo). Pra comparar: o
	/// snapshot INTEIRO do jogo hoje custa 42,5 KB/s de subida com 10 jogadores.
	///
	/// E o numero e quatro porque **ninguem distingue mais de quatro vozes simultaneas** -- a quinta
	/// nao seria informacao, seria ruido caro. Quem cai fora e sempre o mais LONGE, que e quem o
	/// ouvinte menos ia entender de qualquer jeito.
	/// ========================================================================================================
	/// </summary>
	public const int MaxFalantesPorOuvinte = 4;

	// =====================================================================
	// A TORNEIRA
	// =====================================================================
	/// <summary>
	/// O TETO DE UM FALANTE: no maximo <see cref="QuadrosPorSegundo"/> quadros por segundo, com
	/// <see cref="RajadaDeQuadros"/> de folga.
	///
	/// Balde de credito, e nao "um quadro a cada 20 ms": o segundo recusaria a rajada legitima que a
	/// rede produz sozinha. Aqui o credito repoe no ritmo certo e o excesso simplesmente nao cabe --
	/// um cliente que mande 500 quadros por segundo tem 50 aceitos e 450 jogados fora, e nao ha nada
	/// que ele possa fazer a respeito.
	///
	/// PURO de proposito (o tempo entra como parametro): assim a bancada prova o teto sem esperar
	/// segundo nenhum, e nao ha um `DateTime.Now` escondido decidindo regra de jogo.
	/// </summary>
	public sealed class Torneira
	{
		private double _credito = RajadaDeQuadros;
		private long _ultimo;

		/// <summary>Quando alguem falou pela ultima vez -- e o que responde "esta falando agora?".</summary>
		public long UltimoQuadro { get; private set; }

		/// <summary>Quantos quadros ja foram recusados. So a bancada e o log de admin leem.</summary>
		public int Recusados { get; private set; }

		/// <summary>
		/// Este quadro passa? Devolve falso quando o credito acabou ou quando o quadro e grande
		/// demais -- e **as duas recusas sao silenciosas**: avisar custaria uma linha de chat por
		/// quadro recusado, ou seja 450 por segundo no caso que o teto existe pra conter.
		/// </summary>
		public bool Cabe(long agoraMs, int bytes)
		{
			if (bytes <= 0 || bytes > MaxBytesDeQuadro) { Recusados++; return false; }

			if (_ultimo != 0)
				_credito = Math.Min(RajadaDeQuadros,
									_credito + (agoraMs - _ultimo) / 1000.0 * QuadrosPorSegundo);
			_ultimo = agoraMs;

			if (_credito < 1) { Recusados++; return false; }
			_credito -= 1;
			UltimoQuadro = agoraMs;
			return true;
		}

		/// <summary>Esta pessoa esta falando AGORA? Ver <see cref="MsAteCalar"/>.</summary>
		public bool Falando(long agoraMs) => UltimoQuadro != 0 && agoraMs - UltimoQuadro <= MsAteCalar;
	}

	// =====================================================================
	// O QUE O OUVINTE RECEBE PRA DESENHAR O SOM
	// =====================================================================
	/// <summary>
	/// A DISTANCIA, ESPREMIDA NUM BYTE. 0 = colado, 255 = no limite do alcance.
	///
	/// Um byte porque a resolucao que sobra (704 px / 255 = 2,8 px por degrau) e ordens de grandeza
	/// menor do que o ouvido percebe em volume, e porque cada byte aqui e multiplicado por 50 quadros
	/// por segundo por ouvinte.
	/// </summary>
	public static byte DistanciaEmByte(float distancia, float alcance) =>
		alcance <= 0 ? (byte)0
					 : (byte)Math.Clamp((int)MathF.Round(distancia / alcance * 255f), 0, 255);

	/// <summary>O caminho de volta -- a fracao 0..1 do alcance.</summary>
	public static float FracaoDaDistancia(byte b) => b / 255f;

	/// <summary>
	/// VOLUME PELA DISTANCIA, e ele **nao e linear**.
	///
	/// Som real cai com o inverso da distancia, e o ouvido le volume em escala logaritmica: uma queda
	/// linear soa como se nada acontecesse ate a metade do caminho e depois some de uma vez. A curva
	/// aqui e o inverso amortecido -- 1/(1+k*d) --, que e a mesma familia que o `Attenuation` do
	/// `AudioStreamPlayer2D` ja usa nos efeitos do jogo.
	///
	/// NO LIMITE DO ALCANCE ELA NAO CHEGA A ZERO, e isso e proposital: quem esta a 703 px ja e o
	/// ULTIMO que recebe alguma coisa (a 705 o servidor nao manda nada), e um corte em silencio
	/// absoluto faria a voz sumir um passo antes de a pessoa sair do alcance -- o jogador ouviria o
	/// corte como falha de rede. Cai a ~0,17, que e audivel e obviamente distante.
	/// </summary>
	public static float VolumePelaDistancia(float fracao)
	{
		float d = Math.Clamp(fracao, 0f, 1f);
		return 1f / (1f + 5f * d);
	}
}
