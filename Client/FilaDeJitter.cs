using Concentus;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ A FILA DE JITTER: O QUE FICA ENTRE O PACOTE E O ALTO-FALANTE ============================
/// A voz chegava e ia DIRETO pro gerador de audio: `Play()` com a fila vazia e 20 ms empurrados por
/// pacote. A ocupacao alvo era UM quadro -- qualquer atraso maior que 20 ms entre dois pacotes virava
/// zeros no alto-falante (o motor nao avisa, so toca silencio). Como o emissor manda no `_Process`
/// dele (a 60 fps o padrao e 0,1,1,0,1,1 -- ate 17 ms de jitter imposto pelo proprio laco do jogo) e
/// a rede poe o resto, o resultado era exatamente o relato do dono: *"picotando, nao fica fluido"*.
///
/// Esta classe e o amortecedor que faltava. Ela nao sabe o que e Godot: recebe pacotes numerados e,
/// quando o dono dela pergunta *"quantas amostras o gerador ja tem?"*, decide o que entregar. Quem le
/// o gerador de verdade e o `VozOuvida`; quem le um gerador SIMULADO e a bancada (`--diagvoz`) -- e o
/// codigo que decide e o MESMO nos dois, que e o unico jeito de a bancada provar alguma coisa.
///
/// ============================ O RELOGIO E O DO GERADOR, E NAO O DO PACOTE ============================
/// A reproducao e guiada pela OCUPACAO da fila do gerador: a cada `_Process` o dono chama
/// <see cref="Puxar"/> com quantas amostras ainda estao esperando pra tocar, e esta classe completa ate
/// o alvo, EM ORDEM, tirando do buffer de reordenacao. O pacote que chega so entra no buffer; ele
/// nunca toca no gerador. E isso que separa "chegou" de "e a hora de tocar" -- e e essa separacao que
/// absorve o jitter.
/// ====================================================================================================
///
/// ============================ A PRE-CARGA E SILENCIO, E NAO ESPERA ============================
/// Pra converter jitter em atraso constante e preciso tocar cada quadro D ms depois de ele chegar.
/// O jeito mais simples e exato: no comeco de cada fala, empurrar D ms de SILENCIO na frente do
/// primeiro quadro. O ouvinte ouviria silencio de qualquer forma (a fala ainda nao chegou), e quando
/// o primeiro quadro toca ja ha D ms de folga atras dele. Esperar acumular N pacotes daria um atraso
/// que depende do jitter da chegada (que e o que se quer eliminar) e exigiria uma maquina de estados
/// pro caso de os N nunca chegarem.
/// =============================================================================================
///
/// ============================ O ALVO E ADAPTATIVO, E A REGRA E ASSIMETRICA ============================
/// Comeca em <see cref="AlvoInicial"/> (60 ms). Sobe DEPRESSA (+2 quadros por evento) sempre que a fila
/// mostra que nao bastou: secou (<see cref="Underruns"/>), precisou esticar num engasgo que depois
/// acabou (<see cref="Esticados"/>) ou um pacote chegou depois de a vaga dele ja ter sido remendada
/// (<see cref="Atrasados"/>). Desce DEVAGAR (-1 quadro a cada <see cref="MsEstavelAteEncolher"/> de
/// fala sem evento) ate <see cref="AlvoMinimo"/>. Subir rapido e barato (uns ms de atraso); descer
/// rapido custa um picote a cada tentativa -- e o picote e o que a pessoa lembra.
/// ======================================================================================================
///
/// ============================ O QUE ACONTECE COM CADA TIPO DE PACOTE ============================
///   | chegou                                | o que se faz                                              |
///   |---------------------------------------|-----------------------------------------------------------|
///   | em ordem                              | entra na vaga dele; toca quando chegar a vez               |
///   | fora de ordem (ainda dentro da vez)   | entra na vaga dele; toca na ORDEM CERTA                    |
///   | atrasado (a vaga ja foi remendada)    | descartado, e o alvo sobe                                  |
///   | duplicado                             | descartado                                                 |
///   | salto grande (falante relogou)        | fluxo novo: buffer limpo, pre-carga de novo                |
///   | faltando, e o seguinte chegou         | FEC do seguinte (o Opus carrega uma copia grosseira)      |
///   | faltando, e so um mais adiante chegou | PLC (`Decode` sem dados: o decodificador extrapola)       |
///   | faltando, e NADA chegou (engasgo)     | estica com PLC SEM avancar a sequencia, ate 3 quadros     |
///
/// O "estica sem avancar" e a diferenca entre engasgo e perda: no engasgo os pacotes ainda vao chegar,
/// entao a vaga deles NAO e remendada -- o tempo e esticado na frente e eles tocam depois, inteiros.
/// Remendar a vaga faria os pacotes chegarem "atrasados" e serem jogados fora: 60 ms de fala perdidos
/// pra cobrir 60 ms de rede lenta.
/// ============================================================================================
///
/// ============================ DERIVA: SOLTA UM QUADRO, NUNCA TRUNCA ============================
/// Duas placas de som nunca marcam o mesmo 16 kHz; a que manda pode ser um pouco mais rapida, e ai o
/// buffer cresce um quadro a cada tantos segundos. E depois de um engasgo o excesso chega de uma vez.
/// Quando o total guardado passa do alvo em 2 quadros por mais de <see cref="MsDeExcessoAteSoltar"/>,
/// UM quadro pendente e solto (<see cref="Soltos"/>). Um quadro inteiro, e nao um pedaco: cortar no
/// meio de um quadro e um estalo; pular 20 ms de fala uma vez a cada dois segundos nao se ouve. E o
/// excesso some sozinho no fim de cada fala, porque a proxima comeca com pre-carga nova.
/// ==============================================================================================
/// </summary>
public sealed class FilaDeJitter
{
	/// <summary>O que foi entregue ao gerador -- a bancada conta cada tipo.</summary>
	public enum Tipo : byte
	{
		/// <summary>Pre-carga: zeros na frente do primeiro quadro de uma fala.</summary>
		Silencio,
		/// <summary>Um pacote de verdade, decodificado.</summary>
		Normal,
		/// <summary>A vaga estava vazia e o pacote SEGUINTE trazia a copia FEC dela.</summary>
		Fec,
		/// <summary>A vaga estava vazia e so havia pacote mais adiante: o decodificador extrapolou.</summary>
		Plc,
		/// <summary>Engasgo: nada chegou e a fila ia secar. PLC sem avancar a sequencia.</summary>
		Esticado,
	}

	/// <summary>Quem recebe cada quadro pronto: (PCM de 320 amostras, o tipo, a sequencia, distancia, parede).</summary>
	public delegate void Entrega(short[] pcm, Tipo tipo, ushort seq, byte distancia, bool parede);

	private const int Amostras = VozLocal.AmostrasPorQuadro;

	/// <summary>
	/// O ALVO DE PARTIDA: 3 quadros = 60 ms de folga na fila do gerador.
	///
	/// E o menor valor que cobre o jitter que o proprio jogo impoe (ate 17 ms do laco do emissor, ate
	/// 17 ms do laco do receptor) com um quadro de sobra pra rede. Menos que isso a fila seca no
	/// primeiro pacote que chegar um pouco tarde; mais que isso e atraso pago mesmo em rede boa.
	/// </summary>
	public const int AlvoInicial = 3;

	public const int AlvoMinimo = 3;

	/// <summary>
	/// 10 quadros = 200 ms. Acima disso a conversa vira walkie-talkie: as pessoas comecam a falar uma
	/// por cima da outra porque a resposta demora a chegar. Rede que precisa de mais do que isso picota
	/// de qualquer jeito, e picotar um pouco e melhor do que falar com meio segundo de atraso.
	/// </summary>
	public const int AlvoMaximo = 10;

	/// <summary>
	/// QUANTAS VAGAS O BUFFER DE REORDENACAO TEM: 32 quadros = 640 ms.
	///
	/// Potencia de dois QUE DIVIDE 65536, porque a vaga e `seq % Capacidade` e a sequencia e `ushort`:
	/// na virada 65535 -> 0 as vagas continuam consecutivas. Com 30 nao continuariam.
	/// </summary>
	public const int Capacidade = 32;

	/// <summary>
	/// A RESERVA: abaixo de 2 quadros (40 ms) a fila esta prestes a secar e o remendo (FEC/PLC) entra
	/// no lugar do pacote que nao chegou. Acima disso a vaga vazia ESPERA -- e essa espera e o que
	/// deixa um pacote fora de ordem tocar na ordem certa.
	///
	/// Dois quadros e nao um: entre duas visitas do `_Process` passam 17 ms (e 33 num engasgo de um
	/// quadro de tela). Com a reserva em um quadro, a fila secaria entre a visita que decidiu esperar e
	/// a seguinte.
	/// </summary>
	public const int QuadrosDeReserva = 2;

	/// <summary>
	/// DEPOIS DE QUANTO SEM PACOTE O FLUXO E DADO COMO PARADO, em ms.
	///
	/// 100 ms = 5 quadros. O emissor manda ~250 ms de quadros ainda depois de a voz parar (o hangover do
	/// `Microfone`), entao dentro de uma fala nunca ha 100 ms sem pacote; e a proxima fala comeca com
	/// pre-carga nova. Um engasgo de REDE maior que isso vai parecer um fim de fala -- e a retomada e
	/// tratada como continuacao (mesma sequencia), entao o que se perde e so a pre-carga de silencio.
	/// </summary>
	public const long MsDeInatividade = 100;

	/// <summary>
	/// QUANTOS QUADROS DE PLC SEGUIDOS UM ENGASGO PODE ESTICAR: 3 = 60 ms.
	///
	/// O PLC do Opus extrapola o ultimo quadro e vai apagando; depois de tres ja e quase silencio, e
	/// continuar seria gastar CPU pra tocar nada. E no FIM de uma fala o engasgo e legitimo (a pessoa
	/// parou): tres quadros extrapolados do hangover (que ja e quase silencio) nao se ouvem.
	/// </summary>
	public const int MaxEsticadosSeguidos = 3;

	public const long MsDeExcessoAteSoltar = 2000;

	/// <summary>Quanto tempo de fala SEM evento faz o alvo descer um quadro.</summary>
	public const long MsEstavelAteEncolher = 8000;

	/// <summary>
	/// Um engasgo produz varios sintomas em sequencia (esticou, secou, pacote atrasado). Dentro desta
	/// janela eles contam como UM evento -- senao um engasgo so levaria o alvo do minimo ao maximo.
	/// </summary>
	public const long MsDeFolgaEntreEventos = 500;

	/// <summary>
	/// ============================ O DEFEITO INJETADO -- SO BANCADA ============================
	/// Falso em jogo. Ligado, esta classe volta a ser o que o `VozOuvida` era antes dela: o pacote e
	/// decodificado e entregue NA CHEGADA, sem pre-carga, sem reordenacao, sem remendo -- e
	/// <see cref="Puxar"/> nao faz nada. A `--diagvoz` liga isto, alimenta a MESMA sequencia de
	/// pacotes com jitter e exige que o gerador simulado seque. Sem essa linha vermelha, a linha verde
	/// da fila ligada nao provaria que a bancada enxerga um picote.
	/// ===========================================================================================
	/// </summary>
	public static bool SemFilaDeTeste;

	private sealed class Vaga
	{
		public bool Ocupada;
		public ushort Seq;
		public byte N;
		public byte Distancia;
		public bool Parede;
		public readonly byte[] Dados = new byte[VozLocal.MaxBytesDeQuadro];
	}

	private readonly Vaga[] _vagas = new Vaga[Capacidade];
	private readonly IOpusDecoder _decodificador;
	private readonly Entrega _entregar;

	/// <summary>PCM de saida, reusado. Quem recebe consome na hora (a mesma disciplina do `GameClient.VozRecebida`).</summary>
	private readonly short[] _pcm = new short[Amostras];

	/// <summary>A pre-carga. Nunca e escrito: quem recebe nao pode filtrar em cima dele.</summary>
	private readonly short[] _silencio = new short[Amostras];

	/// <summary>A proxima sequencia a tocar.</summary>
	private ushort _proximo;

	/// <summary>Ja houve um comeco de fluxo (a primeira sequencia foi fixada).</summary>
	private bool _tocando;

	/// <summary>O proximo <see cref="Puxar"/> deve refazer a pre-carga (fluxo novo ou retomado).</summary>
	private bool _reiniciar;

	/// <summary>Quadros de silencio ainda por entregar na frente da fala.</summary>
	private int _preCarga;

	private int _pendentes;
	private int _esticadosSeguidos;
	private long _ultimoPacoteMs;
	private long _ultimoPuxarMs;
	private long _ultimoEventoMs;
	private long _excessoDesde;
	private long _msEstavel;

	/// <summary>Os metadados do ultimo pacote de verdade -- e o que os quadros sinteticos carregam.</summary>
	private byte _ultimaDistancia;
	private bool _ultimaParede;

	/// <summary>Quantos quadros de folga a fila do gerador deve manter. Ver o cabecalho.</summary>
	public int Alvo { get; private set; } = AlvoInicial;

	public int Pendentes => _pendentes;

	// ---- os contadores: a bancada le, o resto do jogo ignora ----
	/// <summary>A fila do gerador secou enquanto ainda havia fala (zeros no alto-falante).</summary>
	public int Underruns { get; private set; }
	/// <summary>Quadros de PLC inseridos num engasgo, sem avancar a sequencia.</summary>
	public int Esticados { get; private set; }
	/// <summary>Vagas vazias remendadas com FEC ou PLC (perda de verdade).</summary>
	public int Remendados { get; private set; }
	/// <summary>Pacotes que chegaram depois de a vaga deles ja ter tocado.</summary>
	public int Atrasados { get; private set; }
	public int Duplicados { get; private set; }
	/// <summary>Quadros pendentes soltos pra tirar deriva.</summary>
	public int Soltos { get; private set; }
	public int Reinicios { get; private set; }
	/// <summary>Quadros de VOZ entregues (tudo menos a pre-carga).</summary>
	public int Entregues { get; private set; }

	public FilaDeJitter(IOpusDecoder decodificador, Entrega entregar)
	{
		_decodificador = decodificador;
		_entregar = entregar;
		for (int i = 0; i < Capacidade; i++) _vagas[i] = new Vaga();
	}

	/// <summary>Ha pacote recente? Ver <see cref="MsDeInatividade"/>.</summary>
	public bool Ativa(long agoraMs) => _tocando && agoraMs - _ultimoPacoteMs <= MsDeInatividade;

	// =====================================================================
	// CHEGOU UM PACOTE
	// =====================================================================
	public void Receber(ushort seq, ReadOnlySpan<byte> dados, byte distancia, bool parede, long agoraMs)
	{
		if (dados.Length == 0 || dados.Length > VozLocal.MaxBytesDeQuadro) return;

		if (SemFilaDeTeste)
		{
			// O CAMINHO ANTIGO, inteiro: decodifica e entrega na chegada. Ver o cabecalho da flag.
			int n = _decodificador.Decode(dados, _pcm, Amostras, decode_fec: false);
			if (n > 0) _entregar(_pcm, Tipo.Normal, seq, distancia, parede);
			return;
		}

		bool ativa = Ativa(agoraMs);
		_ultimoPacoteMs = agoraMs;

		// ESTICOU E DEPOIS CHEGOU: era engasgo, nao fim de fala. So agora da pra saber -- e so agora o
		// alvo sobe. Contar na hora de esticar faria o alvo subir a cada fim de frase, porque o fim
		// de frase e indistinguivel de um engasgo enquanto ele acontece.
		if (_esticadosSeguidos > 0 && ativa) Evento(agoraMs);
		_esticadosSeguidos = 0;

		if (!_tocando) Iniciar(seq);
		else
		{
			int d = (short)(ushort)(seq - _proximo);
			if (!ativa)
			{
				// O FLUXO DORMIA. Dentro da janela e a mesma conversa retomada -- e a vaga que faltar
				// entre a ultima tocada e esta NAO e pulada: ela e remendada na vez dela, como
				// qualquer outra. Pular parecia barato (a cauda perdida costuma ser hangover) e engolia
				// fala: num engasgo de rede de 100 ms em que o pacote seguinte chega ANTES dos
				// segurados, pular pra ele descartava os segurados como "atrasados" -- 60 ms de voz
				// jogados fora pra economizar 60 ms de PLC quase mudo. Fora da janela, ou uma
				// sequencia MENOR (o falante relogou e o contador do servidor voltou a zero), e fluxo
				// novo.
				if (d < 0 || d >= Capacidade) Iniciar(seq);
				_reiniciar = true;
			}
			else if (d < 0)
			{
				if (d < -Capacidade) Iniciar(seq);
				else { Atrasados++; Evento(agoraMs); return; }
			}
			else if (d >= Capacidade) Iniciar(seq);
		}

		Vaga v = _vagas[seq % Capacidade];
		if (v.Ocupada)
		{
			// A vaga so pode estar ocupada pelo MESMO numero: qualquer outro estaria fora da janela e
			// teria reiniciado o fluxo acima.
			Duplicados++;
			return;
		}
		v.Ocupada = true;
		v.Seq = seq;
		v.N = (byte)dados.Length;
		v.Distancia = distancia;
		v.Parede = parede;
		dados.CopyTo(v.Dados);
		_pendentes++;
	}

	private void Iniciar(ushort seq)
	{
		Limpar();
		_proximo = seq;
		_tocando = true;
		_reiniciar = true;
		_excessoDesde = 0;
		Reinicios++;
	}

	private void Limpar()
	{
		foreach (Vaga v in _vagas) v.Ocupada = false;
		_pendentes = 0;
	}

	// =====================================================================
	// A HORA DE TOCAR
	// =====================================================================
	/// <summary>
	/// COMPLETA A FILA DO GERADOR ATE O ALVO. Chamado a cada `_Process` do dono.
	/// </summary>
	/// <param name="ocupacao">Quantas amostras o gerador ainda tem pra tocar.</param>
	/// <param name="vagasNoGerador">Quantas amostras ainda cabem nele. Nunca se empurra meio quadro.</param>
	/// <returns>Quantos quadros foram entregues nesta visita.</returns>
	public int Puxar(int ocupacao, int vagasNoGerador, long agoraMs)
	{
		if (!_tocando || SemFilaDeTeste) return 0;

		bool ativa = Ativa(agoraMs);

		// A FILA SECOU COM FALA NO AR: e o picote. Nao conta no reinicio (a fila esta vazia porque a
		// fala esta comecando) nem depois de a esticada desistir (dai o fluxo acabou de verdade).
		if (ocupacao == 0 && !_reiniciar
			&& (_pendentes > 0 || (ativa && _esticadosSeguidos < MaxEsticadosSeguidos)))
		{
			Underruns++;
			Evento(agoraMs);
		}

		if (_reiniciar)
		{
			// A PRE-CARGA E SO O QUE FALTA pra haver `Alvo` quadros na frente: o que ja esta no
			// gerador (o fim da fala anterior) e o que ja chegou em rajada contam. Depois de um
			// engasgo os pacotes vem todos juntos, e por silencio na frente deles sao 60 ms de buraco
			// a mais num buraco que ja existiu.
			_preCarga = Math.Max(0, Alvo - ocupacao / Amostras - _pendentes);
			_reiniciar = false;
			_esticadosSeguidos = 0;
		}

		int alvo = Alvo * Amostras;
		int reserva = QuadrosDeReserva * Amostras;
		int entregues = 0;

		while (vagasNoGerador >= Amostras)
		{
			if (_preCarga > 0)
			{
				if (ocupacao >= alvo) break;
				_entregar(_silencio, Tipo.Silencio, _proximo, _ultimaDistancia, _ultimaParede);
				_preCarga--;
			}
			else if (Presente(_proximo, out Vaga? v))
			{
				if (ocupacao >= alvo) break;
				if (!Tocar(v!)) continue;   // a vaga foi consumida, mas nada entrou no gerador
			}
			else if (_pendentes > 0)
			{
				// A VAGA ESTA VAZIA E HA PACOTE MAIS ADIANTE. Enquanto a reserva permitir, ESPERA: o
				// pacote pode estar so fora de ordem. Quando nao da mais pra esperar, remenda.
				if (ocupacao >= reserva) break;
				if (!Remendar()) continue;
			}
			else
			{
				// NADA CHEGOU: engasgo ou fim de fala. Estica enquanto a reserva pedir, o fluxo
				// parecer vivo e o limite de esticadas nao tiver sido atingido -- SEM avancar a
				// sequencia (ver o cabecalho: engasgo nao e perda).
				if (ocupacao >= reserva || !ativa || _esticadosSeguidos >= MaxEsticadosSeguidos) break;
				int n = _decodificador.Decode(ReadOnlySpan<byte>.Empty, _pcm, Amostras, decode_fec: false);
				if (n <= 0) break;
				_entregar(_pcm, Tipo.Esticado, _proximo, _ultimaDistancia, _ultimaParede);
				Esticados++;
				Entregues++;
				_esticadosSeguidos++;
			}

			ocupacao += Amostras;
			vagasNoGerador -= Amostras;
			entregues++;
		}

		Deriva(ocupacao, agoraMs);
		Encolher(ativa, agoraMs);
		return entregues;
	}

	private bool Presente(ushort seq, out Vaga? vaga)
	{
		Vaga v = _vagas[seq % Capacidade];
		bool ha = v.Ocupada && v.Seq == seq;
		vaga = ha ? v : null;
		return ha;
	}

	/// <summary>Decodifica a vaga da vez e a libera. Falso se o decodificador nao devolveu nada.</summary>
	private bool Tocar(Vaga v)
	{
		int n = _decodificador.Decode(v.Dados.AsSpan(0, v.N), _pcm, Amostras, decode_fec: false);
		_ultimaDistancia = v.Distancia;
		_ultimaParede = v.Parede;
		v.Ocupada = false;
		_pendentes--;
		_proximo++;
		_esticadosSeguidos = 0;
		if (n <= 0) return false;
		_entregar(_pcm, Tipo.Normal, (ushort)(_proximo - 1), v.Distancia, v.Parede);
		Entregues++;
		return true;
	}

	/// <summary>
	/// A VAGA DA VEZ NAO CHEGOU E NAO DA MAIS PRA ESPERAR. Com o SEGUINTE presente, o FEC dele e uma
	/// copia grosseira desta; sem ele, o PLC extrapola. Um so de cada vez: o FEC so carrega o quadro
	/// imediatamente anterior, e a vaga seguinte vai passar por esta mesma decisao na proxima volta.
	/// </summary>
	private bool Remendar()
	{
		ushort seq = _proximo;
		Tipo tipo;
		int n;
		if (Presente((ushort)(seq + 1), out Vaga? seguinte))
		{
			n = _decodificador.Decode(seguinte!.Dados.AsSpan(0, seguinte.N), _pcm, Amostras, decode_fec: true);
			tipo = Tipo.Fec;
		}
		else
		{
			n = _decodificador.Decode(ReadOnlySpan<byte>.Empty, _pcm, Amostras, decode_fec: false);
			tipo = Tipo.Plc;
		}
		_proximo++;
		Remendados++;
		if (n <= 0) return false;
		_entregar(_pcm, tipo, seq, _ultimaDistancia, _ultimaParede);
		Entregues++;
		return true;
	}

	// =====================================================================
	// DERIVA E ADAPTACAO
	// =====================================================================
	private void Deriva(int ocupacao, long agoraMs)
	{
		int excesso = _pendentes + ocupacao / Amostras - Alvo;
		if (excesso < 2) { _excessoDesde = 0; return; }
		if (_excessoDesde == 0) { _excessoDesde = agoraMs; return; }
		if (agoraMs - _excessoDesde < MsDeExcessoAteSoltar) return;

		// SOLTA O MAIS VELHO PENDENTE. Se a vaga da vez esta vazia, pula-la e soltar tambem (e o
		// remendo que ela receberia deixa de existir).
		if (Presente(_proximo, out Vaga? v)) { v!.Ocupada = false; _pendentes--; }
		_proximo++;
		Soltos++;
		_excessoDesde = agoraMs;
	}

	private void Encolher(bool ativa, long agoraMs)
	{
		if (ativa && _ultimoPuxarMs != 0) _msEstavel += Math.Min(agoraMs - _ultimoPuxarMs, 100);
		_ultimoPuxarMs = agoraMs;
		if (_msEstavel < MsEstavelAteEncolher) return;
		_msEstavel = 0;
		Alvo = Math.Max(AlvoMinimo, Alvo - 1);
	}

	private void Evento(long agoraMs)
	{
		_msEstavel = 0;
		if (_ultimoEventoMs != 0 && agoraMs - _ultimoEventoMs < MsDeFolgaEntreEventos) return;
		_ultimoEventoMs = agoraMs;
		Alvo = Math.Min(AlvoMaximo, Alvo + 2);
	}
}
