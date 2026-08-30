namespace Jandirus.Core.World;

/// <summary>
/// ============================ ESTE CORPO ESTA OCUPADO? -- E COM O QUE? ============================
/// *"atualmente ao estar lutando e andando, vc consgue empurrar o inimigo e vise e versa, faca com q
/// n de pra empurar npcs ou outros players ao andar contra eles enquando eles batem ou fazem outra
/// coisa."* -- o pedido do dono, literal.
///
/// ============================ POR QUE UM NOME, E NAO UM `bool` ============================
/// Um `bool Ocupado` responde "ele nao se mexe" e nada mais -- e "o corpo nao saiu do lugar" nao e
/// diagnostico nenhum: sao ONZE estados diferentes, cada um por um motivo legitimo, e de fora eles
/// sao identicos. Este projeto ja pagou essa conta na propria mudez do corpo local, e a resposta foi
/// o <c>LocalPlayer.PorQueNaoAnda</c>, que NOMEIA em vez de devolver um bit. Este arquivo e o irmao
/// dele do lado do OBSTACULO: o `PorQueNaoAnda` responde *"por que EU nao ando"*, este responde
/// *"por que VOCE nao me deixa passar"*.
///
/// E ele e o UNICO lugar onde a lista existe. Escreve-la duas vezes -- uma no servidor pra a grade,
/// outra no cliente pra a previsao -- e promessa de divergirem, e a que mente e sempre a que ninguem
/// le. Por isso: **quem CALCULA e o servidor** (<c>GameServer.OcupacaoDe</c>, um filler so), **o
/// valor VIAJA** (<c>EntityState.Ocupacao</c>) e o cliente **LE a resposta** em vez de refaze-la.
/// ==========================================================================================
///
/// ============================ E POR QUE NAO REUSAR O `PodeMexerOCorpo` ============================
/// Porque ele responde outra pergunta, e a diferenca tem caso real: la dentro estao o ARRASTO do
/// feixe e o PUXAO da fusao, que sao corpos que **estao sendo movidos** -- "nao pode andar" e o
/// oposto de "nao pode ser deslocado". E ele e do SERVIDOR: o cliente nunca teria como responde-lo,
/// entao a metade que faz o andador PARAR na tela (a previsao local) ficaria sem fonte.
/// ==============================================================================================
///
/// ============================ O QUE O DM DIZ ============================
/// O DM **nao tem esta lista**, e nao ter e a resposta dele: `mob/Cross` (`CombatMovement.dm:52-57`)
/// deixa passar SO quem esta `flying` e todo o resto esbarra, e o comentario da linha 51 e explicito
/// -- *"Undense players when they enter in combat, to each other. **Not anymore**"*: a excecao de
/// combate existiu e foi REMOVIDA de proposito. La um corpo ocupado ja era solido pra quem anda,
/// porque `density` nunca e desligado por estado nenhum.
///
/// O que este arquivo acrescenta e o que o DM nao tinha como precisar: **altura**. La voar e um
/// booleano no mesmo `z` e o `Cross` abre pra ele; aqui voar e a maneira normal de se mover numa
/// luta, e "quem voa atravessa" transformava o pedido do dono em letra morta justamente na hora da
/// briga. Ver <see cref="ClasseDeCorpo.Bloqueia"/>.
/// ======================================================================
/// </summary>
public enum Ocupacao : byte
{
	/// <summary>Nao esta fazendo nada que o prenda. Vale a regra antiga inteira.</summary>
	Livre = 0,

	/// <summary>
	/// NOCAUTEADO OU MORTO NO CHAO. Primeiro da lista porque ganha de tudo -- um corpo caido nao
	/// esta atacando nem guardando, e mostrar qualquer outro nome aqui seria mentira.
	///
	/// Ele ja barrava quem anda (o DM nunca desliga `density` por KO nem por morte -- ver
	/// <see cref="ClasseDeCorpo"/>), e continua barrando pela MESMA linha. Entra nesta lista pra
	/// fechar o buraco de cima: caido no ar, ninguem passa por dentro dele voando.
	/// </summary>
	Nocauteado = 1,

	/// <summary>
	/// NUM EMBATE (o ZanzoClash ou o embate de ki). O jogo CONGELA os dois corpos enquanto ele
	/// corre; empurrar um deles com o ombro seria desmanchar a cena por fora.
	/// </summary>
	NoEmbate = 2,

	/// <summary>
	/// NUMA CINEMATICA (a estreia de uma transformacao). Mesmo caso do embate: o corpo esta parado
	/// porque o jogo mandou, e nao porque o jogador escolheu.
	/// </summary>
	EmCena = 3,

	/// <summary>
	/// COM UM CANAL DE KI DE PE -- reunindo o raio ou com ele saindo da mao. O corpo ja esta
	/// FINCADO (o `EnraizadoPorKi` recusa o passo dele); faltava so ele fincar pros outros.
	/// </summary>
	CanalizandoKi = 4,

	/// <summary>
	/// REUNINDO ENERGIA (a tecla C segurada). Tambem ja fincado pelo proprio jogo
	/// (`PodeMexerOCorpo` tem `!pl.Carregando`).
	/// </summary>
	ReunindoKi = 5,

	/// <summary>
	/// NO MEIO DE UM GOLPE (o prazo do `AtaqueAte`). **E o caso que o dono nomeou** --
	/// *"enquando eles batem"*.
	/// </summary>
	Atacando = 6,

	/// <summary>
	/// DE GUARDA ERGUIDA (o ALT). **DECISAO ESCRITA: guardar OCUPA.**
	///
	/// O dono disse *"ou fazem outra coisa"*, e nao ha nada no jogo que seja mais "estar fazendo
	/// uma coisa" que guarda: e um gesto mantido, custa vigor por segundo (`CombatState` conta o
	/// `TempoDeGuarda`), muda o resultado de todo golpe que chega (`MeleeResolver:159` e `:166`) e
	/// e a unica postura do jogo cuja frase inteira e *"eu estou firme aqui"*. Um adversario que
	/// pudesse desmanchar isso com o ombro tiraria o sentido da tecla.
	/// </summary>
	Guardando = 7,

	/// <summary>
	/// SEGURANDO ALGUEM. Dois corpos amarrados por um aperto; desloca-los pelo ombro moveria o
	/// par, e um deles nao esta na grade (o carregado no colo sai dela -- ver
	/// `GameServer.EntraNaGrade`).
	/// </summary>
	Agarrando = 8,

	/// <summary>TREINANDO (`Ficha.train`). Postura mantida, e ela ja tem pose propria no fio.</summary>
	Treinando = 9,

	/// <summary>MEDITANDO (`Ficha.med`). Idem.</summary>
	Meditando = 10,
}

/// <summary>
/// A LISTA -- e o unico lugar em que ela existe. Ver <see cref="Ocupacao"/>.
/// </summary>
public static class CorpoOcupado
{
	/// <summary>
	/// OS SINAIS CRUS, do jeito que quem pergunta os tem na mao.
	///
	/// E um `struct` de bools e nao dez parametros soltos pelo motivo de sempre neste projeto: dez
	/// parametros do mesmo tipo sao dez chances de trocar dois de lugar, e o compilador nao acusa
	/// nenhuma. Quem preenche e o servidor, num lugar so (`GameServer.OcupacaoDe`).
	/// </summary>
	public struct Sinais
	{
		/// <summary>`Ficha.KO` ou morto ainda deitado no mundo dos vivos.</summary>
		public bool Nocauteado;

		/// <summary>ZanzoClash ou embate de ki.</summary>
		public bool NoEmbate;

		/// <summary>Cinematica de transformacao (`EmCena`).</summary>
		public bool EmCena;

		/// <summary>Um canal de ki de pe (`CanalDeKiDe`).</summary>
		public bool CanalizandoKi;

		/// <summary>A tecla C segurada (`ServerPlayer.Carregando`).</summary>
		public bool ReunindoKi;

		/// <summary>Dentro do prazo do `AtaqueAte`.</summary>
		public bool Atacando;

		/// <summary>`CombatState.Bloqueando` -- o ALT.</summary>
		public bool Guardando;

		/// <summary>`AgarrandoId != 0`.</summary>
		public bool Agarrando;

		/// <summary>`Ficha.train`.</summary>
		public bool Treinando;

		/// <summary>`Ficha.med`.</summary>
		public bool Meditando;
	}

	/// <summary>
	/// ============================ A ORDEM E A REGRA ============================
	/// A cadeia e de PRIORIDADE, exatamente como a do `ServerPlayer.Pose` e a do
	/// `LocalPlayer.PorQueNaoAnda`: o primeiro que responder ganha, e o nome que sai e o que o
	/// jogador diria olhando pra o corpo. Um corpo nocauteado que ainda tinha `train` ligado nao e
	/// "treinando"; um corpo com raio na mao que tambem estava de guarda e "canalizando".
	///
	/// **PRA A COLISAO A ORDEM NAO MUDA NADA** (todo valor diferente de <see cref="Ocupacao.Livre"/>
	/// barra igual). Ela existe pro nome: quando alguem perguntar "por que eu nao passo por aqui",
	/// a resposta tem que ser a certa de primeira, e nao a primeira que couber.
	/// ==========================================================
	/// </summary>
	public static Ocupacao De(in Sinais s) =>
		  s.Nocauteado    ? Ocupacao.Nocauteado
		: s.NoEmbate      ? Ocupacao.NoEmbate
		: s.EmCena        ? Ocupacao.EmCena
		: s.CanalizandoKi ? Ocupacao.CanalizandoKi
		: s.ReunindoKi    ? Ocupacao.ReunindoKi
		: s.Atacando      ? Ocupacao.Atacando
		: s.Guardando     ? Ocupacao.Guardando
		: s.Agarrando     ? Ocupacao.Agarrando
		: s.Treinando     ? Ocupacao.Treinando
		: s.Meditando     ? Ocupacao.Meditando
		: Ocupacao.Livre;

	/// <summary>Ocupado e "qualquer coisa menos livre". Uma linha, e ela mora aqui pra ninguem
	/// escrever `!= Ocupacao.Livre` a mao em cinco lugares e um deles virar `== Ocupacao.Livre`.</summary>
	public static bool Ocupado(Ocupacao o) => o != Ocupacao.Livre;

	/// <summary>
	/// O NOME, em portugues -- o molde do <c>PorQueNaoAnda</c>: **vazio quer dizer livre**.
	///
	/// Ele nao e enfeite de log. E o que a bancada imprime quando uma linha fica vermelha, e a
	/// diferenca entre *"o corpo nao andou"* (que nao diz nada) e *"o corpo parou porque o outro
	/// estava de guarda"* (que diz onde procurar).
	/// </summary>
	public static string Nome(Ocupacao o) => o switch
	{
		Ocupacao.Nocauteado    => "nocauteado (ou morto no chao)",
		Ocupacao.NoEmbate      => "num embate",
		Ocupacao.EmCena        => "numa cinematica de transformacao",
		Ocupacao.CanalizandoKi => "com um canal de Ki de pe",
		Ocupacao.ReunindoKi    => "reunindo energia (C)",
		Ocupacao.Atacando      => "no meio de um golpe",
		Ocupacao.Guardando     => "de guarda erguida (ALT)",
		Ocupacao.Agarrando     => "segurando alguem",
		Ocupacao.Treinando     => "treinando",
		Ocupacao.Meditando     => "meditando",
		_ => "",
	};

	/// <summary>
	/// O VALOR QUE VEIO DO FIO, saneado. Um byte desconhecido (cliente de outra versao, pacote
	/// corrompido) vira <see cref="Ocupacao.Livre"/> e nao um estado inventado -- errar pro lado de
	/// "nao barra" e o unico erro que nao prende ninguem.
	/// </summary>
	public static Ocupacao DeByte(byte b) => b <= (byte)Ocupacao.Meditando ? (Ocupacao)b : Ocupacao.Livre;
}
