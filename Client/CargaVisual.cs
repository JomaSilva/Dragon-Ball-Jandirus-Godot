using Godot;

namespace Jandirus.Client;

/// <summary>
/// O QUE SE VE E SE OUVE de alguem reunindo energia.
///
/// ============================ NAO HA MAIS LUZ AQUI ============================
/// A primeira versao acendia um <see cref="PointLight2D"/>. O dono cortou, duas vezes e cada vez
/// mais claro: "o brilho causado por aura e so quando ESTA TRANSFORMADO, na base aura nao gera
/// brilho". Ele esta certo e a razao e a mesma que fez a <see cref="Aura"/> nascer desligada:
/// se todo mundo que aperta uma tecla ilumina o cenario, a luz para de significar "aquele ali
/// esta transformado" -- que e a unica coisa que ela deveria dizer.
///
/// Entao o que sobrou aqui e o DESENHO (<see cref="SpriteDeAura"/>) e o SOM. A luz mora so no
/// caminho da transformacao.
/// ==============================================================================
///
/// ============================ E QUEM MANDA ACENDER E O SERVIDOR ============================
/// A versao anterior acendia direto da TECLA, sem perguntar nada. Resultado que o dono viu: sem
/// Ki Unlocked o servidor recusava a carga e o jogador ACENDIA MESMO ASSIM -- luz e som pra uma
/// coisa que nao estava acontecendo, e o Ki parado. Pior: quem estava do lado nao via nada, porque
/// os OUTROS ja eram desenhados pela verdade do servidor (o snapshot).
///
/// Agora as duas pontas leem a mesma fonte. A tecla so PEDE (`SendCarregar`); o servidor decide e
/// avisa; isto obedece. E o mesmo principio do resto do jogo -- o cliente calcula o que da pra
/// conferir, e pergunta o que nao da.
/// ==========================================================================================
/// </summary>
public partial class CargaVisual : Node2D
{
	/// <summary>Cor da carga comum: o branco-azulado do ki cru.</summary>
	private static readonly Color CorCarga = new(0.62f, 0.80f, 1.0f);

	/// <summary>Cor de quem passou dos 100%: puxa pro dourado -- o corpo esta reclamando.</summary>
	private static readonly Color CorExcesso = new(1.0f, 0.84f, 0.42f);

	private SpriteDeAura _desenho = null!;
	private AudioStreamPlayer2D? _laco;

	private bool _ligado, _excesso;
	private double _fase;

	/// <summary>
	/// A FORMA MANDA. Enquanto houver transformacao acesa, este desenho nao aparece: a aura da
	/// forma ja esta la, e duas auras empilhadas foi exatamente o defeito que o `AuraCheck` do
	/// original desarma ("o power-up NAO empilha a aura base por cima"). Escrito pelo World, no
	/// mesmo lugar em que a forma acende a dela.
	/// </summary>
	public bool FormaAcesa;

	public override void _Ready()
	{
		_desenho = new SpriteDeAura { Name = "Desenho" };
		AddChild(_desenho);
		SetProcess(false);
	}

	/// <summary>
	/// O SOM E O DESENHO SAO SEPARADOS, e essa separacao veio de uma correcao do dono.
	///
	/// ============================ O DM DIVIDE, E EU TINHA JUNTADO ============================
	/// No original a AURA e o estalo estao dentro de `else if(canPower && stamina > 1)`
	/// (Meditate.dm:181) -- mas o `poweruprunning = 1`, que e quem liga o LACO do
	/// `aurapowered.wav`, esta na linha 193, FORA do gate.
	///
	/// Eu vi isso, chamei de defeito do original e amarrei as tres camadas ao mesmo estado. O dono
	/// jogou e disse: "carregar o ki ta sem som, deveria tocar o mesmo som q toca no byond em
	/// looping". Ele esta certo e a divisao do DM faz sentido: o zumbido e o RETORNO de que a
	/// energia esta subindo -- quem so tem Ki Unlocked precisa dele, porque e a unica coisa que
	/// confirma que a tecla fez algo. A aura e outra coisa: e sinal de controle de verdade.
	///
	/// (O que continua NAO copiado e o som pra quem o servidor RECUSOU -- sem Ki Unlocked nao ha
	/// `carregando`, e portanto nao ha zumbido. No DM ha, e ali e defeito mesmo.)
	/// =======================================================================================
	/// </summary>
	public void Som(bool ligado)
	{
		if (ligado == _comSom) return;
		_comSom = ligado;

		if (!ligado) { PararLaco(); return; }
		AudioDirector.EfeitoNoLugar(this, Trilha.CargaInicio, 0.55f);
		LigarLaco();
	}

	private bool _comSom;

	/// <summary>
	/// O DESENHO da aura de carga. Chamado a cada snapshot, entao a primeira linha e a comparacao
	/// e nao o trabalho.
	/// </summary>
	public void Definir(bool carregando, bool excesso)
	{
		_excesso = excesso;
		if (carregando == _ligado) { Pintar(); return; }
		_ligado = carregando;

		if (_ligado) Acender();
		else Apagar();
	}

	private void Acender()
	{
		_fase = 0;
		SetProcess(true);
		Pintar();
	}

	/// <summary>
	/// O ZUMBIDO CONTINUO, num player dedicado.
	///
	/// ============================ POR QUE O LACO ESTAVA PICOTADO ============================
	/// A versao anterior fazia `wav.LoopEnd = wav.Data.Length / 2`, com um comentario dizendo
	/// "16 bits por amostra, mono". O comentario descrevia o ARQUIVO e nao o RECURSO: os 49 .wav
	/// deste projeto sao importados com `compress/mode=2`, ou seja QOA -- `Data` e bitstream
	/// COMPRIMIDO, nao PCM. Deram 6960/2 = 3480 contra 17124 quadros reais: o laco repetia os
	/// primeiros 0,158 s de um som de 0,777 s, ~6 vezes por segundo. Foi isso que o dono ouviu
	/// como "toca varias vezes e nao em loop".
	///
	/// `GetLength() * MixRate` e AGNOSTICO DE FORMATO -- o proprio recurso sabe quanto dura,
	/// esteja em PCM, IMA-ADPCM ou QOA. Nao ha divisor pra acertar, e por isso nao ha divisor
	/// pra errar de novo quando alguem trocar o modo de importacao.
	/// =======================================================================================
	/// </summary>
	private void LigarLaco()
	{
		var fluxo = ResourceLoader.Load<AudioStream>(Trilha.CargaLaco);
		if (fluxo == null) { GD.PushWarning($"[carga] som ausente: {Trilha.CargaLaco}"); return; }

		if (fluxo is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
		{
			// DUPLICA ANTES DE MEXER: o ResourceLoader entrega a instancia do CACHE, compartilhada
			// por todo mundo. Mutar em cima mudaria o som pra qualquer outro uso dele.
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
			fluxo = wav;
		}

		_laco = new AudioStreamPlayer2D
		{
			Stream = fluxo,
			Bus = AudioDirector.BusEfeitos,
			MaxDistance = 560,
			Attenuation = 1.5f,
			VolumeDb = Mathf.LinearToDb(0.42f),
		};
		AddChild(_laco);
		_laco.Play();
	}

	private void Apagar()
	{
		SetProcess(false);
		_desenho.Definir(false, CorCarga);
	}

	private void PararLaco()
	{
		if (_laco == null) return;
		_laco.Stop();
		_laco.QueueFree();
		_laco = null;
	}

	public override void _Process(double delta)
	{
		// PULSA. Desenho de intensidade fixa le como "estado"; o que respira le como ESFORCO em
		// andamento, que e o que carregar e. Acima dos 100% pulsa mais rapido -- o corpo esta
		// segurando mais do que cabe.
		_fase += delta * (_excesso ? 9.0 : 5.5);
		Pintar();
	}

	private void Pintar()
	{
		if (!_ligado || FormaAcesa) { _desenho.Definir(false, CorCarga); return; }

		float onda = 0.5f + 0.5f * Mathf.Sin((float)_fase);
		float forca = _excesso ? 0.95f + onda * 0.55f : 0.45f + onda * 0.30f;
		_desenho.Definir(true, _excesso ? CorExcesso : CorCarga, forca);
	}
}
