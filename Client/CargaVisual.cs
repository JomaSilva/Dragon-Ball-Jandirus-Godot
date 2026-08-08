using Godot;

namespace Jandirus.Client;

/// <summary>
/// O QUE SE VE E SE OUVE de alguem reunindo energia.
///
/// ============================ A LUZ NAO MORA AQUI, MAS SAI DAQUI ============================
/// A primeira versao acendia um <see cref="PointLight2D"/> PROPRIO. Isso foi cortado e continua
/// cortado -- duas luzes no mesmo corpo somam energia e lavam o sprite, e cada regra ligada numa e
/// esquecida na outra virou defeito. A `PointLight2D` e UMA e mora na <see cref="Aura"/>.
///
/// O que mudou foi de quem ela obedece. Enquanto a regra era "so brilha quem esta TRANSFORMADO", a
/// luz respondia a forma e este node so desenhava. Depois que a forma passou a apenas PREPARAR a
/// aura (ordem do dono: "e pra ela vir desativada e so ativar se o ki passar de 100% ou eu apertar
/// C"), quem desenha a chama passou a ser ESTE node -- e a luz ficou orfa, presa numa condicao que
/// ninguem mais satisfazia. A chama do C saia sem iluminar nada no escuro.
///
/// Entao aqui ficam o DESENHO (<see cref="SpriteDeAura"/>) e o SOM, e a chama avisa a `Aura` por
/// `ChamaDaCarga` -- mesma forca, mesmo instante. A COR nao viaja nesse aviso: ela ja e da `Aura`
/// (<see cref="Aura.CorDaChama"/>), e este node a LE em vez de a mandar.
///
/// E A LUZ NAO VOLTA PRA CA. O aviso acima acende a `PointLight2D` da `Aura` SO SE HOUVER FORMA
/// (dono: "na base n importa a % do ki, a unica coisa q deve ficar ativa e o node carga"). Logo, na
/// base este node desenha e NAO ilumina -- e essa e a leitura certa da frase, nao "mudar a luz de
/// dono": uma luz aqui seria um terceiro dono do mesmo efeito e acenderia justamente na base, que e
/// a queixa. Ver a guarda em `Aura.Aplicar`.
/// ==========================================================================================
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
	// ============================ AS DUAS CORES DESTE NODE FORAM DELETADAS ============================
	// Aqui moravam `CorCarga` (o branco-azulado do ki cru) e `CorExcesso` (um dourado fixo pra quem
	// passou dos 100%). As duas sairam, e a queixa que as matou foi do dono, na BASE: "quando o ki
	// passa de 100% ele fica brilhando de outra cor" -- ele esperava "a mesma de sempre".
	//
	// A raiz nao era o tom escolhido, era haver TRES respostas pra "de que cor e a chama deste
	// corpo": a da forma (guardada no node `Aura`), a de carga e a de sobrecarga daqui. Quem vencia
	// era quem escrevesse por ultimo, e como este node repinta todo quadro enquanto o C esta
	// segurado, quem vencia era sempre este -- inclusive por cima da cor de uma transformacao.
	//
	// Hoje ha UMA: `Aura.CorDaChama`, escrita pela forma e com o ki cru (`Aura.CorDoKiCru`) como
	// padrao, porque um corpo sem forma ESTA na base. Deste node so sai a FORCA -- ver `Pintar`.
	// ================================================================================================

	private SpriteDeAura _desenho = null!;
	private AudioStreamPlayer2D? _laco;

	private bool _ligado, _excesso;
	private double _fase;

	/// <summary>O desenho proprio deste node. Pra bancada -- ele NAO e o mesmo do node `Aura`.</summary>
	public SpriteDeAura DesenhoDeTeste => _desenho;

	/// <summary>
	/// ============================ A TESTEMUNHA INDEPENDENTE DA SOBRECARGA ============================
	/// O bit `EntityState.Sobrecarregado` tem DOIS consumidores no cliente: este node (a chama) e o
	/// contorno (`World.MarcarSobrecarga` -> `AplicarContorno`). A bancada que julga o contorno do
	/// corpo ALHEIO precisa saber se aquele corpo esta acima dos 100% -- e perguntar isso ao
	/// `_sobrecarregados` seria perguntar ao proprio reu: se o funil inteiro estivesse morto, os dois
	/// lados da checagem responderiam "nao ha sobrecarga" e ela passaria com o jogo quebrado.
	///
	/// Ler pelo OUTRO consumidor fecha isso. Os dois saem do mesmo bit no mesmo `AoReceberSnapshot`,
	/// mas por caminhos separados -- e sao justamente os dois caminhos que passaram anos discordando
	/// (o contorno era escrito pela FORMA enquanto a chama ja obedecia ao Ki).
	/// ==============================================================================================
	/// </summary>
	public bool ExcessoDeTeste => _excesso;

	/// <summary>O outro bit do mesmo par (`Carregando`): ele esta com o C segurado. Pra bancada.</summary>
	public bool CarregandoDeTeste => _ligado;

	/// <summary>
	/// ============================ A CARGA TAMBEM PRECISA DA FOLHA DA FORMA ============================
	/// Este node monta o PROPRIO <see cref="SpriteDeAura"/>, separado do node <see cref="Aura"/> --
	/// e por isso ele nao herdou nada quando a troca de folha foi ligada la. O dono viu o resultado
	/// exato: "ao carregar o C transformado ele ta usando a aura da base, e isso forca com q a aura
	/// do super saiyajin vire o mesmo sprite da base porem pintado de amarelo".
	///
	/// Dois desenhos da mesma coisa, e eu so tinha ensinado um. Agora quem troca a forma avisa os
	/// dois -- `World.PrepararAuraDaForma` e `Transformacao.Assumir`, os mesmos dois lugares que
	/// escrevem a folha do node `Aura`.
	/// ================================================================================================
	///
	/// (Quem traduz o simbolo em caminho e a <see cref="SpriteDeAura.CaminhoDa"/>: a tabela estava
	/// copiada aqui e na <see cref="Aura"/>, e a chama da cinematica seria a terceira copia.)
	/// </summary>
	public void Folha(Jandirus.Core.Forms.FolhaDeAura f) => _desenho.DefinirFolha(f);

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

	/// <summary>
	/// SOLTOU O C. Passa pelo `Pintar` em vez de apagar o desenho na mao, e isso e o conserto de um
	/// vazamento: apagar aqui deixava de fora o aviso a <see cref="Aura"/>, entao quem soltava o C
	/// nunca DEVOLVIA a vez -- a aura de forma ficava suprimida e (desde que a luz passou a seguir a
	/// chama) a `PointLight2D` ficava acesa pra sempre. Dois lugares apagando a mesma coisa era o
	/// jeito de a proxima regra nascer so num deles; agora ha um caminho de apagar so.
	/// </summary>
	private void Apagar()
	{
		SetProcess(false);
		Pintar();   // o ramo `!_ligado`: apaga o desenho E devolve a chama, com a luz, pra `Aura`
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
		// ============================ E A FORMA NAO CALA MAIS ESTE DESENHO ============================
		// A primeira linha daqui era `if (!_ligado || FormaAcesa)`: um campo `FormaAcesa`, escrito pelo
		// World e pela cinematica, suprimia a chama da carga quando a forma tinha aura propria. Fazia
		// sentido no desenho antigo, em que transformar ja acendia uma, e duas auras empilhadas ficavam
		// feias.
		//
		// A regra mudou por pedido do dono ("e pra ela vir desativada e so ativar se o ki passar de 100%
		// ou eu apertar C"): com a forma so PREPARANDO a aura, a guarda parou de proteger de coisa
		// nenhuma e virou o defeito -- segurar C na base funcionava e em Super Saiyajin nao acendia
		// nada. O campo foi DELETADO junto com a guarda; deixa-lo escrito e nunca lido so daria a quem
		// vier depois um fato sem consumidor pra confiar. Quem impede as duas chamas hoje e a
		// `Aura.ChamaDaCarga` la embaixo, e ela e a mesma linha que acende a luz.
		// ==========================================================================================

		// A COR VEM DA `Aura`, E ELA E A UNICA. Ver o bloco das duas cores deletadas la em cima: este
		// node so responde POR QUANTO a chama esta forte, nunca por que cor ela tem. UMA busca por
		// pintada e nao duas -- as duas escritas abaixo (desenho e luz) tem que sair do mesmo node,
		// senao voltam a poder discordar.
		Aura? aura = GetParent()?.GetNodeOrNull<Aura>("Aura");
		// SEM IRMA `Aura` nao ha luz nem cor guardada; o padrao e o MESMO campo que ela usa, pra os
		// dois nao poderem divergir num corpo montado pela metade. (Em jogo nunca acontece: os dois
		// nodes nascem juntos, `World.cs:1241-1242`.)
		Color cor = aura?.CorDaChama ?? Aura.CorDoKiCru;

		if (!_ligado)
		{
			_desenho.Definir(false, cor);
			aura?.ChamaDaCarga(false, 0);   // devolve a vez
			return;
		}

		float onda = 0.5f + 0.5f * Mathf.Sin((float)_fase);
		// A SOBRECARGA CONTINUA SE DISTINGUINDO, so que pela FORCA e nao pela cor: ela pulsa mais
		// forte aqui e mais rapido no `_Process`. E a leitura certa -- passar dos 100% nao troca a
		// energia do corpo, so aperta mais dela no mesmo lugar; o corpo esta RECLAMANDO. Trocar o
		// tom dizia "outro tipo de ki", que e o que o dono estranhou.
		// A FAIXA DA CARGA COMUM SUBIU (era 0,45..0,75). Com a `forca` governando so o ALFA, 0,45
		// virava uma chama quase invisivel em vez de uma chama fraca -- e o que se quer e presenca
		// discreta, nao ausencia. 0,70..0,95 le como "esta reunindo energia" sem competir com o
		// excesso, que continua estourando a cor (acima de 1).
		float forca = _excesso ? 0.95f + onda * 0.55f : 0.70f + onda * 0.25f;

		// FOLHA JA COLORIDA IGNORA A COR (ver `SpriteDeAura.SemTinta` e o uniform `tingir`): num
		// Super Saiyajin a chama sai dourada da arte, e nao do que vai aqui. A cor continua sendo
		// mandada porque quem decide se ela vale e o shader -- mandar branco aqui APAGARIA a arte,
		// que foi o defeito da rodada passada.
		// QUEM ESTA DESENHANDO SOU EU, E POR ISSO A LUZ SEGUE A MINHA CHAMA: a aura de forma cala o
		// desenho e ACENDE a luz com a forca que vai aqui. Uma chama por corpo, uma luz por corpo --
		// foi ficar so com a metade "cala o desenho" que deixou a aura sem brilho no escuro ao
		// segurar C. Ver `Aura.ChamaDaCarga`.
		//
		// NA BASE ESTE AVISO NAO ACENDE NADA, e e de proposito: a `Aura` so deixa a chama da carga
		// virar luz se houver FORMA (a guarda `_temForma`, em `Aura.Aplicar`). Aqui se manda a forca
		// do mesmo jeito -- quem decide e a `Aura`, dona da luz, e nao este node.
		aura?.ChamaDaCarga(true, forca);
		_desenho.Definir(true, cor, forca);
	}
}
