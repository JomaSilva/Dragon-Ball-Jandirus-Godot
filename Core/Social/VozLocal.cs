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
	/// 24 kHz mono -- a banda "super-wide" do Opus (12 kHz de audio). Era 16 kHz.
	///
	/// ============================ POR QUE SUBIU (dono, 2026-09-05) ============================
	/// *"a qualidade dele nao ta muito boa, nao ta picotando mas parece que ta sempre um pouco
	/// abafado"*. Abafado e exatamente o que 8 kHz de banda soa: a inteligibilidade mora nas
	/// consoantes (o "s", o "f", o "t"), que vivem de 4 a 10 kHz, e a 16 kHz metade delas nem entra
	/// -- o anti-alias do microfone corta antes. 24 kHz e a taxa seguinte que o Opus aceita (8, 12,
	/// 16, 24, 48) e a primeira em que a fala soa "de perto"; 48 seria musica, e a voz concorre com
	/// o snapshot do jogo no mesmo cano. Custa 50% mais amostras por quadro (480) e NADA no fio: o
	/// tamanho do quadro e o bitrate, nao a taxa.
	/// ==========================================================================================
	/// </summary>
	public const int TaxaDeAmostragem = 24000;

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

	/// <summary>480 amostras (24 kHz x 20 ms). E o que o codificador recebe e o que o decodificador devolve.</summary>
	public const int AmostrasPorQuadro = TaxaDeAmostragem * MsPorQuadro / 1000;

	/// <summary>
	/// A TAXA EM QUE SE OUVE: 48 kHz, e nao a de captura. **MEDIDO** (2026-09-05, Concentus 2.2.2, C#
	/// puro, tom de 440 Hz a 0,5 de amplitude, dez quadros de 20 ms): o decodificador criado a 24 kHz
	/// (ou a 16) devolve SILENCIO pra um fluxo codificado em super-wide (24) ou fullband (48) -- 480
	/// amostras de zero, sem erro nenhum --, enquanto o mesmo fluxo decodificado a 48 kHz volta inteiro,
	/// e um fluxo de 16 kHz decodifica em qualquer taxa:
	///
	///   | codifica -> decodifica  | RMS de volta |
	///   |-------------------------|--------------|
	///   |  16 -> 16 / 24 / 48     |  0,35 (ok)   |
	///   |  24 -> 48               |  0,35 (ok)   |
	///   |  24 -> 24 / 16          |  0,000       |
	///   |  48 -> 24 / 16          |  0,000       |
	///
	/// E o caminho de DESCIDA do decodificador do Concentus que nao faz o que promete. A saida: o
	/// microfone fala em 24 kHz (o `Reamostrador` so decima, e 48 -> 24 e 2 pra 1) e todo ouvinte
	/// decodifica em 48 kHz -- a taxa nativa do Opus e a da placa de quase todo mundo, entao o gerador
	/// do Godot nem precisa reamostrar. O fio nao muda: o pacote Opus e o mesmo; quem escolhe a taxa e
	/// cada ponta. A `--diagvoz` codifica em 24 e decodifica em 48, como o jogo.
	/// </summary>
	public const int TaxaDeSaida = 48000;

	/// <summary>960 amostras (48 kHz x 20 ms): o que o decodificador devolve e o que o gerador toca.</summary>
	public const int AmostrasPorQuadroDeSaida = TaxaDeSaida * MsPorQuadro / 1000;

	/// <summary>50 quadros por segundo. E o teto honesto de quem fala -- ver <see cref="Torneira"/>.</summary>
	public const int QuadrosPorSegundo = 1000 / MsPorQuadro;

	/// <summary>
	/// 24 kbit/s, VBR. Era 16 -- e a 16 **o FEC nao existia**.
	///
	/// ============================ O LIMIAR DO FEC, MEDIDO ============================
	/// O SILK so codifica a copia redundante (LBRR) do quadro anterior quando a taxa alvo passa de um
	/// limiar que depende da banda e da perda declarada: em banda larga com 10% e 16000 x 1,15 =
	/// 18,4 kbit/s. A 16 kbit/s a copia nunca era escrita, `decode_fec` degenerava em PLC do quadro
	/// ANTERIOR, e todo comentario deste sistema que prometia "o seguinte carrega o perdido" era falso.
	/// **MEDIDO** na `--diagvoz` (tons alternados por quadro, o FEC do pacote k+2 tem que devolver o
	/// tom do quadro k+1):
	///
	///   | kbit/s | B/quadro | FEC certo |
	///   |--------|----------|-----------|
	///   |   16   |   43,2   |   0/10    |
	///   |   20   |   57,7   |   9/10    |
	///   | **24** | **64,8** | **9/10**  |
	///   |   28   |   71,8   |   9/10    |
	///
	/// 24 e o primeiro com folga sobre o limiar (20 passa por 1,2 kbit/s, e quem mexer na perda
	/// declarada o derruba). Custa ~1,1 KB/s a mais por fluxo -- e compra o unico remendo de perda que
	/// existe num canal sem retransmissao, e mais qualidade de voz de brinde.
	///
	/// O resto da tabela de custo (complexidade), medido com Opus (Concentus, C# puro), quadro de
	/// 20 ms, a 16 kbit/s:
	///
	///   | compl | codificar | % de um quadro |
	///   |-------|-----------|----------------|
	///   |   0   |   135 us  |     0,68%      |
	///   | **3** | **102 us**|   **0,51%**    |
	///   |   8   |   284 us  |     1,42%      |
	///
	/// Contra os vizinhos: PCM cru 16 kHz = 640 B/quadro (**31,2 KB/s**), ADPCM 4 bits = 160 B
	/// (**7,8 KB/s**), Opus a 24 kbit/s com FEC = ~65 B (**3,2 KB/s**). Dez vezes mais barato que o
	/// cru e duas vezes e meia mais barato que o ADPCM -- e o ADPCM nao tem FEC.
	///
	/// **E o servidor nao paga codec nenhum**: ele repassa o payload OPACO e so decide quem recebe.
	/// Por isso a economia vale onde a banda multiplica -- no leque de ouvintes.
	///
	/// SUBIU PRA 32 (2026-09-05) JUNTO COM A TAXA: a tabela acima foi medida em banda larga (16 kHz).
	/// Em super-wide o Opus so enche os 12 kHz de banda a partir de ~28 kbit/s -- abaixo disso ele
	/// volta sozinho pra banda larga e os 24 kHz nao compram nada. 32 e o primeiro degrau com folga,
	/// bem acima do limiar do FEC (que a `--diagvoz` continua medindo). ~4 KB/s por fluxo.
	/// </summary>
	public const int BitsPorSegundo = 32000;

	/// <summary>
	/// Complexidade 5. Era 3: a medida de entao (16 kbit/s, banda larga) mostrava zero byte de ganho
	/// acima de 3 -- mas byte nao e o que muda. O que a complexidade compra no Opus e a ANALISE (busca
	/// de pitch mais fina, o modo hibrido SILK+CELT em super-wide), e e isso que faz os 12 kHz de banda
	/// valerem. Cinco e o meio da escala: custava 70% mais CPU que 3 na medida antiga (0,5% -> ~0,9%
	/// de um quadro de 20 ms), ainda invisivel; oito custava 180% e ai a bateria do jogador pagaria.
	/// </summary>
	public const int Complexidade = 5;

	/// <summary>
	/// TETO DE TAMANHO DE UM QUADRO NO FIO. Quadro maior que isto e DESCARTADO sem dó.
	///
	/// O Opus a 32 kbit/s com FEC da ~85 B; 200 e duas vezes e meia isso, folga pra um pico de VBR num
	/// "sss" (o codificador respeita o teto, mas cravar o teto num quadro e perder qualidade nele) e
	/// ainda assim longe de virar cano (cabe no `byte n` do pacote). **Sem este teto um cliente
	/// modificado vira torneira**: ele mandaria 4 KB por "quadro" e o servidor os multiplicaria pelo
	/// numero de ouvintes. O teto e do SERVIDOR e nao do cliente, porque a unica coisa que um cliente
	/// modificado nao faz e se limitar.
	/// </summary>
	public const int MaxBytesDeQuadro = 200;

	// =====================================================================
	// O TETO POR FALANTE
	// =====================================================================
	/// <summary>
	/// QUANTOS QUADROS DE CREDITO ALGUEM PODE ACUMULAR -- a folga da torneira.
	///
	/// Quinze quadros (300 ms). Era cinco, e cinco recusava voz honesta: o cliente manda por quadro de
	/// TELA, entao um quadro de tela longo (uma zona carregando, um pico de coleta de lixo) despeja de
	/// uma vez o que acumulou -- e a recusa e SILENCIOSA, o ouvinte so ouve o buraco. O emissor agora
	/// se limita sozinho a 5 por visita (`Microfone.QuadrosProntos`), e o balde e o triplo disso pra
	/// duas visitas juntas pela rede ainda caberem. Trezentos ms continuam sendo teto de verdade: um
	/// cliente modificado guarda no maximo 15 quadros de credito e depois anda a 50 por segundo como
	/// todo mundo.
	/// </summary>
	public const double RajadaDeQuadros = 15;

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
	/// (a 3,85 KB/s por fluxo: 49 B de quadro + 28 B de UDP/IP, 50x por segundo, quando o Opus estava
	/// a 16 kbit/s; a 24 kbit/s com FEC sao ~5 KB/s por fluxo, 400 KB/s no pior caso da tabela). Pra
	/// comparar: o snapshot INTEIRO do jogo hoje custa 42,5 KB/s de subida com 10 jogadores.
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
