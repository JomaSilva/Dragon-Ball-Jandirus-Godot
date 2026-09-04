using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO DESVIO (`--diagdesvio`). O que o dono pediu: *"falta o SOM do dodge e o EFEITO DE
/// DESVIO q tinha no byond"*.
///
/// ============================ POR QUE ELA PRECISA DE DOIS PROCESSOS ============================
/// Esquiva e coisa de DOIS corpos: e o passo 1 do `MeleeResolver` (`Etechnique/Espeed * BpModulus`),
/// e sem alguem socando ela nao existe. Chamar `EsquivaZanzoken.Trocar` na mao provaria o EFEITO --
/// que nunca esteve em duvida, a arte esta no disco e importada --, e nao o que estava: se a esquiva
/// ACIONA o efeito. Por isso aqui nao se chama nada de desenho: quem desenha e o `World.AoGolpe`,
/// a partir do `HitEvent` que o servidor mandou.
///
/// E ela precisa de DESNIVEL DE PODER, senao nao ha esquiva nenhuma pra medir -- dois personagens
/// recem-criados acertam 100% dos socos, e isso e a regra do DM (`CombatMovement.dm:190`). Quem
/// abre o desnivel e o `--esquivateste N` do servidor, que da N vezes o BP ao HOST.
///
/// ============================ AS PERGUNTAS ============================
///   1. a esquiva ACONTECE?              -- conta os desfechos que chegam pelo `HitEvent`
///   2. o CORPO SOME e VOLTA?            -- ver o bloco abaixo; e a pergunta mais cara do arquivo
///   3. a arte e PRETA e sai SEM TINTA?  -- mede o pixel do `Zanzoken.tres` e confere que o node
///      que o desenha nao tem `Material` nem `Modulate`
///   4. onde nascem os nos da camada `Atores`? -- a faisca do desvio e do ATACANTE, e este e o
///      numero que diz se ela nasceu no corpo certo
///   5. o som sai, e sai UMA VEZ POR ESQUIVA?   -- `AudioDirector.Espiao` com carimbo de QUADRO,
///      cruzado com o quadro do golpe: som em quadro sem golpe = som por quadro; dois `meleeflash`
///      no mesmo golpe = som dobrado; zero = mudo
///   6. da pra DISTINGUIR de um acerto?         -- o TRIPTICO abaixo contra `desvio-acerto.png`,
///      mesmos corpos, mesma camera, mesmo atraso de obturador
///
/// ============================ O TRIPTICO: A UNICA FOTO QUE RESPONDE O PEDIDO ============================
/// O pedido do dono nao e um instante, e uma SEQUENCIA: *"o personagem q desviou deveria ficar
/// INVISIVEL, as LINHAS PRETAS aparecerem ONDE O CORPO DELE ESTA, e dps as linhas iam sumir e o
/// CORPO DELE APARECER DNV"*. Uma foto so no auge do efeito mostra o meio da frase e cala os dois
/// extremos -- e o extremo que nao pode dar errado e o ultimo. Entao saem TRES fotos por esquiva:
///
///   1-antes    o corpo LA, nenhum efeito rodando
///   2-durante  o corpo SUMIDO, as listras pretas no lugar dele
///   3-depois   o corpo DE VOLTA
///
/// A do "antes" nao da pra tirar quando a esquiva chega -- nesse instante o efeito ja comecou. Por
/// isso a bancada guarda o ULTIMO QUADRO LIMPO (corpo visivel, efeito nenhum, os dois corpos ja
/// colados) e o grava quando o desvio acontece: e literalmente o quadro anterior ao desvio.
///
/// E o triptico sai DUAS VEZES, no chao e NO AR (`SobeAos`), porque as listras nascem onde o corpo
/// e DESENHADO e voando o desenho sobe ate 160 px acima do no -- um triptico so no chao passaria
/// verde com o efeito plantado nos pes de um personagem que esta no ceu.
/// =========================================================================================================
///
/// ============================ POR QUE A 2 E A QUE IMPORTA ============================
/// O efeito virou uma TROCA: o corpo do defensor SOME e as listras do `Zanzoken` aparecem no lugar
/// dele (o `flick` do DM -- ver `EsquivaZanzoken`). Isso poe em jogo um defeito que nao existia
/// antes: **a invisibilidade vazar**. Um corpo que some e nao volta e infinitamente pior que um
/// efeito feio, e esta bancada mede 82% de esquiva contra alguem dez vezes mais forte -- varias por
/// segundo, sobrepostas. Entao ela conta, QUADRO A QUADRO, quatro coisas que so podem dar zero:
///
///   * quadro com efeito rodando e o corpo AINDA VISIVEL   (a queixa do dono, de volta)
///   * quadro SEM efeito e o corpo INVISIVEL               (o vazamento -- corpo perdido)
///   * DOIS nodes de efeito no mesmo corpo                 (dono unico quebrado; ver `Trocar`)
///   * corpo invisivel no fim da bancada                   (vazou e ninguem devolveu)
///
/// Esta e a leitura que a foto NAO da: uma foto no auge do efeito nao distingue "some e volta" de
/// "some pra sempre", e e justamente o segundo que nao pode acontecer.
/// ====================================================================================
///
/// COMO RODAR -- dois processos. O HOST tem janela (a foto e o juiz); o adversario e headless:
///
///   host (forte, so apanha e fotografa):
///     Godot --path . --host --rede 7986 --vooteste --esquivateste 10 --diagdesvio --nome Alvo --conta desvio_a
///   adversario (fraco, soca sem parar, e sobe junto na hora certa):
///     Godot --headless --path . --rede 7986 --connect 127.0.0.1 --socar --socarvoando 34 --nome Soco --conta desvio_b
///
/// Ver `testar-desvio.bat`, que sobe os dois.
/// </summary>
public partial class RoboDeDesvio : Node
{
	private static GameClient? C => GameClient.Instance;

	/// <summary>Um efeito de som que realmente tocou, com o QUADRO em que tocou.</summary>
	private readonly List<(ulong Quadro, double T, string Arquivo, float Vol)> _sons = [];

	/// <summary>Um golpe relatado pelo servidor CONTRA MIM, com o quadro em que chegou.</summary>
	private readonly List<(ulong Quadro, double T, Jandirus.Core.Combat.Desfecho D, bool Zanzo)> _golpes = [];

	/// <summary>
	/// Os quadros em que chegou um golpe QUALQUER, meu ou dos outros.
	///
	/// A conferencia "som de melee em quadro sem golpe" e o que separa *uma vez por esquiva* de
	/// *uma vez por quadro*, e ela nao pode olhar so os meus golpes: a zona tem NPC brigando, o
	/// `World.AoGolpe` desenha e SOA a briga alheia igual, e a primeira rodada desta bancada
	/// acusou 51 sons "soltos" que eram o socador apanhando do Krillin do outro lado do berco.
	/// </summary>
	private readonly HashSet<ulong> _quadrosComGolpe = [];

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];

	private double _t;
	private bool _acabou;

	/// <summary>Quanto tempo a bancada fica no ar antes de fechar o relatorio.</summary>
	private const double Duracao = 80;

	/// <summary>
	/// O SEGUNDO EM QUE OS DOIS SOBEM. Tem que casar com o `--socarvoando` do outro processo (ver
	/// `testar-desvio.bat`): a regra de alcance por altura e assimetrica, e se so um subir os socos
	/// param de chegar e nao ha esquiva nenhuma no ar pra fotografar.
	///
	/// Depois do triptico do chao (que sai por volta dos 15 s) e o mais cedo possivel depois dele --
	/// e nao "com folga", que foi como a primeira rodada errou. VOAR CUSTA KI, 6 por segundo pela
	/// formula do DM (`Voo.CustoPorSegundo`), e o tanque nao e infinito: subir aos 34 s deixava os
	/// DOIS cairem de exaustao antes de um unico soco chegar la em cima, e a bancada mediu zero
	/// esquiva no ar com os dois corpos de volta no chao. O que se ganha aqui e tempo de tanque.
	/// </summary>
	private const double SobeAos = 22;

	/// <summary>O segundo da foto da TROCA LONGA -- depois de dezenas de esquivas sobrepostas.</summary>
	private const double TrocaLongaAos = Duracao - 4;

	/// <summary>
	/// O ATRASO DO OBTURADOR, em segundos. A troca de corpo dura 0,30 s (os tres quadros do `flick`
	/// do DM) e a faisca do atacante 0,16 s -- fotografar em 0,08 s pega os DOIS vivos, que e o unico
	/// instante em que a foto responde "da pra entender que ele desviou?". (Havia um terceiro, o anel
	/// de 0,50 s; ele saiu do desvio a pedido do dono -- ver `World.AoGolpe`.)
	///
	/// O MESMO NUMERO PRA OS DOIS DESFECHOS, e isso e o ponto da conferencia 6: comparar uma foto de
	/// esquiva no auge com uma de acerto ja apagando nao provaria contraste nenhum.
	/// </summary>
	private const double Obturador = 0.08;

	private double _esperaAcerto = -1;
	private int _fotosAcerto;

	/// <summary>DUAS de cada desfecho. Uma foto pode calhar de sair com o outro corpo atras de uma
	/// arvore ou no quadro em que o socador ainda esta chegando; duas resolvem sem custo nenhum.</summary>
	private const int FotosPorDesfecho = 2;

	// ============================ O TRIPTICO ============================
	// Ver o cabecalho. Tres fotos por esquiva, e a do "antes" e o quadro ANTERIOR ao desvio.

	/// <summary>
	/// O ULTIMO QUADRO LIMPO: corpo visivel, efeito nenhum, os dois corpos ja colados. E a foto do
	/// "antes", e ela so pode ser tirada ANTES -- quando o `HitEvent` da esquiva chega, o efeito ja
	/// esta no corpo. Guardada junto com a posicao do corpo NA TELA daquele instante, senao o recorte
	/// ampliado sairia mirando onde o corpo esta AGORA (a camera anda entre um quadro e outro).
	/// </summary>
	private Image? _limpa;
	private Vector2 _limpaNaTela;
	private double _proximaLimpa;

	/// <summary>Quanto falta pro obturador do "durante". Negativo = nao ha triptico em curso.</summary>
	private double _esperaDurante = -1;

	/// <summary>Esperando o corpo VOLTAR pra tirar a terceira foto (ela nao tem hora, tem condicao).</summary>
	private bool _querDepois;
	private double _idadeDoDepois;

	/// <summary>Ha quanto tempo o corpo esta de volta sem interrupcao. Ver o atraso de um quadro.</summary>
	private double _repouso;

	/// <summary>De qual triptico sao as fotos que estao saindo agora: `chao` ou `ar`.</summary>
	private string _fase = "chao";
	private bool _tripticoNoChao, _tripticoNoAr;

	/// <summary>O que a foto do "durante" pegou -- e o que a bancada le NO LUGAR de olhar a foto.</summary>
	private readonly List<string> _oQueODuranteViu = [];

	// ============================ O AR ============================
	/// <summary>Ja pedi pra subir? O pedido vai pelo mesmo canal da tecla do jogador.</summary>
	private bool _pediVoo;
	private float _alturaMaxima;
	private int _esquivasNoAr;

	/// <summary>Todo id que ja apareceu num snapshot -- a plateia da conferencia final de visibilidade.</summary>
	private readonly HashSet<int> _todosOsIds = [];
	private bool _fotoDaTrocaLonga;

	/// <summary>O menor vao ja visto entre os dois corpos. So pro relatorio dizer POR QUE nao houve foto.</summary>
	private float _menorVao = 9999;

	/// <summary>Os nos da camada `Atores` que ja existiam no quadro anterior.</summary>
	private readonly HashSet<ulong> _conhecidos = [];

	/// <summary>Quantos quadros ainda vou olhar os nos novos por causa de uma esquiva.</summary>
	private int _inspecionar;
	private readonly List<string> _nosDaEsquiva = [];
	private bool _inspecionei;

	// ============================ OS CONTADORES DA TROCA DE CORPO ============================
	// Ver o cabecalho: os quatro primeiros so podem dar zero. Os dois ultimos existem pra provar
	// que a bancada realmente VIU o efeito -- todos zerados tambem "passaria", e passaria mudo.
	private int _quadrosComEfeito, _quadrosSemEfeito;
	private int _corpoVisivelComEfeito, _corpoInvisivelSemEfeito, _efeitoDuplicado;
	private ulong _idDoEfeito;
	private int _trocasVistas;
	private int _quadrosComTinta, _quadrosSemArte;

	private int _idDoOutro;

	public override void _Ready()
	{
		if (C is not { } cli) return;
		cli.Golpe += AoGolpe;
		cli.SnapshotReceived += Avistou;

		// O ESPIA DO SOM. Nao ha como ouvir num teste, entao o que se registra e a CHAMADA: arquivo,
		// volume e o quadro. Ver `AudioDirector.Espiao`.
		AudioDirector.Espiao = (arq, vol) => _sons.Add((Engine.GetProcessFrames(), _t, arq, vol));

		GD.Print("[desvio] no ar. Esperando alguem socar -- eu NAO revido (o alvo tem que so apanhar).");
	}

	public override void _ExitTree()
	{
		if (C is { } cli) { cli.Golpe -= AoGolpe; cli.SnapshotReceived -= Avistou; }
		AudioDirector.Espiao = null;
	}

	/// <summary>
	/// QUEM E O ADVERSARIO -- e a resposta vem do GOLPE, nao do snapshot.
	///
	/// A primeira versao pegava "o primeiro corpo que nao sou eu" no snapshot, e num berco povoado
	/// isso e um NPC: o vao medido dava 428 px enquanto o socador batia colado, e o portao da foto
	/// (que so dispara abaixo de 96 px) nunca abriu. Quem esta socando ja vem carimbado no proprio
	/// `HitEvent.Atacante` -- nao ha por que adivinhar. O snapshot fica so como rede de seguranca
	/// pro caso de o relatorio sair antes do primeiro golpe.
	/// </summary>
	private void Avistou(List<EntityState> estados)
	{
		if (C is not { } cli) return;
		// A PLATEIA. Ver `NinguemFicouInvisivel`: o vazamento e um corpo que some e nao volta, e nada
		// garante que o corpo perdido seja o meu -- e por isso a conferencia final olha TODO MUNDO
		// que ja passou por um snapshot, e nao so os dois da briga.
		foreach (EntityState e in estados) _todosOsIds.Add(e.Id);

		if (_idDoOutro != 0) return;
		foreach (EntityState e in estados)
			if (e.Id != cli.LocalId) { _idDoOutro = e.Id; break; }
	}

	// =====================================================================
	// O GOLPE
	// =====================================================================
	private void AoGolpe(Protocol.HitEvent h)
	{
		if (C is not { } cli) return;
		_quadrosComGolpe.Add(Engine.GetProcessFrames());
		if (h.Alvo != cli.LocalId) return;   // dali pra baixo, so o que cai em MIM
		_idDoOutro = h.Atacante;             // quem soca em mim E o adversario -- ver `Avistou`

		var d = (Jandirus.Core.Combat.Desfecho)h.Desfecho;
		_golpes.Add((Engine.GetProcessFrames(), _t, d, h.ZanzoEsquiva));

		// A FOTO SO DEPOIS QUE OS DOIS ESTAO COLADOS. O robo de soco chega correndo, e um golpe do
		// primeiro segundo pega os corpos ainda se aproximando -- a foto sairia com o efeito num
		// canto e o outro corpo fora do enquadramento, o que nao responde "da pra entender?".
		float vao = Vao();
		_menorVao = Math.Min(_menorVao, vao);
		if (_t < 6 || vao > 96) return;

		if (d == Jandirus.Core.Combat.Desfecho.Esquivou)
		{
			bool noAr = (World.Instancia?.AlturaDeTeste ?? 0f) > 0f;
			if (noAr) _esquivasNoAr++;
			PedirTriptico(noAr);
			_inspecionar = 2;   // dois quadros: o efeito nasce neste, e o `_Process` pode ja ter passado
		}
		else if (d is Jandirus.Core.Combat.Desfecho.Acertou or Jandirus.Core.Combat.Desfecho.Critico
				 && _fotosAcerto < FotosPorDesfecho && _esperaAcerto < 0)
			_esperaAcerto = Obturador;
	}

	/// <summary>
	/// COMECA UM TRIPTICO nesta esquiva -- se ainda faltar o desta fase e nao houver outro em curso.
	///
	/// A PRIMEIRA FOTO SAI AQUI, e nao daqui a pouco: ela e o quadro ANTERIOR ao desvio, que so
	/// existe porque a bancada o guardou (<see cref="GuardarOQuadroLimpo"/>). Sem quadro limpo
	/// guardado nao ha triptico -- e melhor esperar a proxima esquiva (elas vem varias por segundo)
	/// do que gravar um "antes" que na verdade e um "durante" de outro desvio.
	/// </summary>
	private void PedirTriptico(bool noAr)
	{
		if (_esperaDurante > 0 || _querDepois) return;              // um de cada vez
		if (noAr ? _tripticoNoAr : _tripticoNoChao) return;         // esta fase ja tem o dela
		if (_limpa == null) return;

		_fase = noAr ? "ar" : "chao";
		if (noAr) _tripticoNoAr = true; else _tripticoNoChao = true;

		Salvar(_limpa, _limpaNaTela, $"user://desvio-{_fase}-1-antes.png", $"{_fase.ToUpperInvariant()} 1/3 ANTES");
		_esperaDurante = Obturador;
	}

	/// <summary>Distancia DESENHADA entre os dois corpos. Zero quando falta um.</summary>
	private float Vao()
	{
		if (C is not { } cli || World.Instancia is not { } m) return 9999;
		if (m.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return 9999;
		if (m.PosicaoDesenhadaDe(_idDoOutro) is not { } ele) return 9999;
		return eu.DistanceTo(ele);
	}

	// =====================================================================
	// O QUADRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		_t += delta;

		AfastarDoBerco(delta);
		Subir();
		VarrerOsNosNovos();
		OlharOMeuCorpo();
		GuardarOQuadroLimpo(delta);
		SeguirOTriptico(delta);

		// A TROCA LONGA: uma foto depois de dezenas de esquivas sobrepostas, que e o instante em que
		// um vazamento de invisibilidade ja teria acontecido se fosse acontecer. Os contadores dizem
		// que nao vazou; esta foto e o que o dono consegue conferir com o olho.
		if (!_fotoDaTrocaLonga && _t >= TrocaLongaAos)
		{
			_fotoDaTrocaLonga = true;
			Fotografar("user://desvio-troca-longa.png", "TROCA LONGA (depois de tudo)");
		}

		if (_esperaAcerto > 0 && (_esperaAcerto -= delta) <= 0)
		{
			_esperaAcerto = -1;
			if (Fotografar($"user://desvio-acerto{_fotosAcerto + 1}.png", "ACERTO")) _fotosAcerto++;
		}

		if (_t < Duracao) return;

		// ============================ ESPERA UM QUADRO QUIETO PRA FECHAR ============================
		// As duas conferencias finais ("o corpo esta visivel de novo", "ninguem ficou invisivel")
		// perguntam pelo REPOUSO, e o cronometro nao sabe disso. Com 116 esquivas em 80 s o efeito
		// esta rodando em 44% dos quadros, e a primeira rodada com o triptico fechou o relatorio
		// DENTRO de um desvio: a bancada reprovou o proprio corpo por estar corretamente invisivel a
		// 0,1 s de voltar. Contar corpo escondido POR UM EFEITO VIVO como vazamento e trocar a
		// pergunta -- o vazamento e corpo apagado SEM efeito, e disso quem cuida e o contador por
		// quadro, que deu zero em 9 mil quadros.
		//
		// A espera tem teto. Se em 3 s nunca houver um quadro sem efeito nenhum, o relatorio sai
		// mesmo assim e as conferencias reprovam -- que e o certo: efeito que nunca acaba E o defeito.
		// ============================================================================================
		if (EfeitoEmAlguem() && _esperandoQuieto < 3)
		{
			_esperandoQuieto += delta;
			return;
		}

		_acabou = true;
		Relatar();
	}

	/// <summary>
	/// SAI DE CIMA DO PONTO DE NASCIMENTO, nos primeiros segundos e antes de o socador entrar.
	///
	/// ============================ O VAO ZERO NAO ERA O DASH ============================
	/// Tres rodadas desta bancada mediram os dois corpos na MESMA coordenada, sempre a mesma:
	/// (7984, 8016). Nao era a investida empilhando -- o `Aproximar` para a um tile de proposito
	/// ("encostado, nao POR CIMA", `GameServer.Combat.cs:459`) e o cliente parava antes disso. Era
	/// mais simples: OS DOIS NASCEM NO MESMO PONTO DO BERCO. O socador acordava com a distancia ja
	/// em zero, achava que estava no alcance e nunca dava um passo.
	///
	/// Entao quem anda sou EU, uma vez so, no comeco. Pelas TECLAS (`Input.ActionPress`), pelo mesmo
	/// caminho do jogador -- movimento local, pedido de passo, validacao do servidor. Dai em diante
	/// fico parado e quem se aproxima e ele, que e o que a foto precisa mostrar.
	/// ==================================================================================
	/// </summary>
	private void AfastarDoBerco(double delta)
	{
		const double Comeca = 1.0, Termina = 5.0;   // o socador so entra por volta dos 12 s
		if (_andei) return;
		if (_t >= Comeca && _t < Termina) { Input.ActionPress("move_right"); return; }
		if (_t < Termina) return;
		Input.ActionRelease("move_right");
		_andei = true;
		GD.Print($"[desvio] sai do berco: agora estou em {World.Instancia?.PosicaoLocal}");
	}

	private bool _andei;

	// =====================================================================
	// O AR
	// =====================================================================
	/// <summary>
	/// LEVANTA VOO aos <see cref="SobeAos"/> segundos, pelo mesmo canal da tecla do jogador.
	///
	/// O socador sobe sozinho no mesmo segundo (`--socarvoando`), e os dois juntos e o requisito:
	/// pela regra assimetrica do alcance, quem esta no chao nao acerta quem paira. Se so um subisse,
	/// os golpes parariam de chegar e a fase aerea da bancada mediria silencio.
	///
	/// A altura maxima vista fica guardada porque ela e a UNICA prova de que a fase aerea existiu --
	/// sem `--vooteste` o servidor recusa o voo, e o relatorio precisa dizer isso em vez de anunciar
	/// um "triptico no ar" que na verdade saiu no chao.
	/// </summary>
	private void Subir()
	{
		if (World.Instancia is { } m)
		{
			_alturaMaxima = Math.Max(_alturaMaxima, m.AlturaDeTeste);
			if (m.AlturaDeTeste > 0f) _segundosNoAr += GetProcessDeltaTime();
		}
		if (_pediVoo || _t < SobeAos) return;
		_pediVoo = true;
		C?.SendHabilidade("voar");

		// O TANQUE, DITO EM VOZ ALTA NA HORA DA DECOLAGEM. O voo cobra `Voo.CustoPorSegundo` (6 Ki/s
		// com a skill do `--vooteste`), e um tanque pequeno derruba os dois antes de o primeiro soco
		// chegar la em cima -- foi o que aconteceu na primeira rodada, e o relatorio so dizia "zero
		// esquiva no ar", que aponta pro socador quando o culpado era o Ki. Com o numero aqui, a
		// proxima pessoa le a causa em vez de adivinhar.
		double ki = C?.Sheet.Ki ?? 0, max = C?.Sheet.MaxKi ?? 0;
		double porSegundo = Jandirus.Core.World.Voo.CustoPorSegundo(Jandirus.Core.World.Voo.HabilidadeNivel1, false);
		GD.Print($"[desvio] pedi pra subir -- Ki {ki:0}/{max:0}, o voo cobra {porSegundo:0.0}/s"
				 + $" => da pra ~{(porSegundo > 0 ? ki / porSegundo : 0):0} s de ar (sem contar regen)");
	}

	/// <summary>Quanto tempo o meu corpo passou fora do chao. E o que diz se a fase aerea existiu.</summary>
	private double _segundosNoAr;

	// =====================================================================
	// O TRIPTICO
	// =====================================================================
	/// <summary>
	/// GUARDA O ULTIMO QUADRO LIMPO -- o que vira a foto do "antes".
	///
	/// So vale quadro que responde a pergunta do dono: corpo VISIVEL, efeito NENHUM e os dois corpos
	/// ja colados (o mesmo portao das outras fotos -- um "antes" com o socador ainda chegando nao
	/// serve de comparacao pro "durante"). Fora isso e so ritmo: uma leitura de tela a cada 0,15 s,
	/// que e mais que suficiente pra ter sempre um quadro fresco quando a esquiva chega.
	/// </summary>
	private void GuardarOQuadroLimpo(double delta)
	{
		if ((_proximaLimpa -= delta) > 0) return;
		_proximaLimpa = 0.15;

		if (_t < 6 || Vao() > 96) return;
		if (EfeitoNoMeuCorpo() != null) return;
		if (MeuVisual() is not { Visible: true }) return;

		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return;
		_limpa = img;
		_limpaNaTela = OndeEstouNaTela();
	}

	/// <summary>
	/// AS DUAS FOTOS QUE FALTAM do triptico em curso.
	///
	/// A do "durante" tem HORA (o obturador, no meio dos 0,30 s da troca). A do "depois" tem
	/// CONDICAO, e nao hora: ela so vale com o efeito fora e o corpo de volta -- numa troca de socos
	/// as esquivas se sobrepoem, e um cronometro fixo cairia dentro do desvio SEGUINTE e fotografaria
	/// um corpo invisivel como se fosse o "depois". Se o corpo nao voltar, a foto nao sai, e a
	/// ausencia dela vira falha no relatorio: e exatamente o vazamento que esta bancada persegue.
	/// </summary>
	private void SeguirOTriptico(double delta)
	{
		if (_esperaDurante > 0 && (_esperaDurante -= delta) <= 0)
		{
			_esperaDurante = -1;
			EsquivaZanzoken? efeito = EfeitoNoMeuCorpo();
			bool corpo = MeuVisual() is { Visible: true };
			// O QUE A FOTO PEGOU, EM TEXTO. A foto prova pro olho; isto prova pro relatorio, e e o que
			// impede um "durante" tirado tarde demais (com o efeito ja acabado) de passar por prova.
			_oQueODuranteViu.Add($"{_fase}: efeito rodando = {(efeito != null ? "SIM" : "NAO")}"
								 + $", corpo visivel = {(corpo ? "SIM (ERRADO)" : "nao (certo)")}"
								 + $", altura = {World.Instancia?.AlturaDeTeste ?? 0f:0} px"
								 + $", sobrou na tela: {OQueSobrouDeMim()}"
								 + $", vizinhos: {QuemMaisEstaAqui()}");
			Fotografar($"user://desvio-{_fase}-2-durante.png", $"{_fase.ToUpperInvariant()} 2/3 DURANTE");
			_querDepois = true;
			_idadeDoDepois = 0;
		}

		if (!_querDepois) return;
		_idadeDoDepois += delta;
		// depois dos 0,30 s da troca, no primeiro quadro em que o corpo REALMENTE voltou
		if (_idadeDoDepois < 0.35) return;
		if (EfeitoNoMeuCorpo() != null || MeuVisual() is not { Visible: true }) { _repouso = 0; return; }

		// ============================ A TELA ATRASA UM QUADRO ============================
		// `GetViewport().GetTexture().GetImage()` devolve o ULTIMO QUADRO DESENHADO, e o que se le
		// aqui e o estado dos NODES agora. No primeiro quadro em que o corpo volta, a imagem
		// disponivel ainda e a do quadro anterior -- e a foto do "depois" saiu com as listras na tela
		// e o corpo ausente, exatamente o oposto do que ela existe pra mostrar. Foi assim que a
		// segunda rodada desta bancada produziu um "depois" indistinguivel do "durante".
		//
		// Entao o repouso e CONFIRMADO por alguns quadros antes do obturador. Se uma esquiva nova
		// entrar no meio, o contador zera na linha acima e a espera recomeca.
		// ================================================================================
		if ((_repouso += delta) < 0.10) return;

		_querDepois = false;
		_repouso = 0;
		Fotografar($"user://desvio-{_fase}-3-depois.png", $"{_fase.ToUpperInvariant()} 3/3 DEPOIS");
	}

	/// <summary>
	/// O QUE DO MEU CORPO CONTINUA DESENHADO durante a troca -- por nome de node.
	///
	/// ============================ A PERGUNTA QUE A FOTO NAO RESPONDE ============================
	/// O dono perguntou se sobrou "aura, contorno, cabelo ou rabo flutuando". Numa foto de briga isso
	/// e indecidivel: a zona tem NPC brigando e caido, e num amontoado o pedaco que aparece ao lado
	/// das listras pode ser o rabo do defensor ou o cabelo do vizinho -- olhando, nao da pra separar.
	/// Esta lista separa: ela le os FILHOS DO MEU CORPO, e ninguem mais. Vazia = o que aparece na
	/// foto pertence a outra pessoa. Com nome dentro = e meu, e e defeito.
	///
	/// O balao de fala e a marca de alvo entram aqui de proposito quando estiverem visiveis: eles
	/// declaram <see cref="INaoSomeComOCorpo"/> e ficar e o certo deles -- mas a linha tem que dizer
	/// que eles ficaram, senao a proxima pessoa passa meia hora procurando de quem e o risco na tela.
	/// ==========================================================================================
	/// </summary>
	private string OQueSobrouDeMim()
	{
		if (C is not { } cli || World.Instancia?.CorpoDeTeste(cli.LocalId) is not { } eu) return "(sem corpo)";
		var nomes = new List<string>();
		foreach (Node f in eu.GetChildren())
		{
			if (f is EsquivaZanzoken or Camera2D) continue;
			if (f is CanvasItem { Visible: true } ci)
				nomes.Add($"{ci.Name}<{ci.GetType().Name}>" + (f is INaoSomeComOCorpo ? "(declarado)" : " <<< NAO DECLARADO"));
		}
		return nomes.Count == 0 ? "NADA" : string.Join(" + ", nomes);
	}

	/// <summary>
	/// QUEM MAIS ESTA DESENHADO PERTINHO DE MIM, e a que distancia.
	///
	/// A outra metade da leitura acima: um corpo alheio a 20 px do meu aparece DENTRO do recorte
	/// ampliado, e por Y-sort pode ate ficar na FRENTE do meu. Sem esta linha, um NPC caido ao lado
	/// vira "sobrou um pedaco do defensor" na leitura de quem olha a foto.
	/// </summary>
	private string QuemMaisEstaAqui()
	{
		if (C is not { } cli || World.Instancia is not { } m) return "";
		if (m.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return "";
		var perto = new List<string>();
		foreach (int id in _todosOsIds)
		{
			if (id == cli.LocalId || m.PosicaoDesenhadaDe(id) is not { } onde) continue;
			float d = eu.DistanceTo(onde);
			if (d <= 96) perto.Add($"id {id} a {d:0} px");
		}
		return perto.Count == 0 ? "ninguem a menos de 96 px" : string.Join(", ", perto);
	}

	/// <summary>Quanto tempo o fim da bancada esperou por um quadro sem efeito nenhum na tela.</summary>
	private double _esperandoQuieto;

	/// <summary>
	/// TEM EFEITO DE ESQUIVA RODANDO EM ALGUEM? -- a plateia inteira, nao so eu.
	///
	/// E o portao do fim da bancada: as conferencias de visibilidade so fazem sentido num quadro em
	/// que ninguem esta legitimamente escondido. Olhar so o meu corpo deixaria passar o caso que a
	/// conferencia da plateia existe pra pegar (um corpo alheio escondido no ultimo instante).
	/// </summary>
	private bool EfeitoEmAlguem()
	{
		if (World.Instancia is not { } m) return false;
		foreach (int id in _todosOsIds)
		{
			if (m.CorpoDeTeste(id) is not { } corpo || !GodotObject.IsInstanceValid(corpo)) continue;
			if (corpo.GetNodeOrNull<EsquivaZanzoken>(EsquivaZanzoken.NomeDoNode) != null) return true;
		}
		return false;
	}

	/// <summary>O node do efeito no meu corpo, ou nulo. Um so -- ver `EsquivaZanzoken.Trocar`.</summary>
	private EsquivaZanzoken? EfeitoNoMeuCorpo()
	{
		if (C is not { } cli || World.Instancia?.CorpoDeTeste(cli.LocalId) is not { } eu) return null;
		foreach (Node f in eu.GetChildren())
			if (f is EsquivaZanzoken e) return e;
		return null;
	}

	/// <summary>A pilha do meu personagem (corpo, roupa, cabelo, contorno, rabo) -- o termometro.</summary>
	private CharacterVisual? MeuVisual()
		=> C is { } cli && World.Instancia?.CorpoDeTeste(cli.LocalId) is { } eu
		   ? eu.GetNodeOrNull<CharacterVisual>("Visual") : null;

	/// <summary>Onde o meu corpo esta DESENHADO, em pixels de tela. Ver `Recorte`.</summary>
	private Vector2 OndeEstouNaTela()
	{
		if (C is not { } cli || World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu)
			return Vector2.Zero;
		return (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * eu;
	}

	// =====================================================================
	// O CORPO SOME E VOLTA?
	// =====================================================================
	/// <summary>
	/// A CONFERENCIA DE CADA QUADRO. Quem desvia sou EU (o host forte), entao o corpo a olhar e o
	/// meu -- e olhar o meu e o que torna a leitura possivel: e o unico corpo cujo node a bancada
	/// alcanca sem adivinhar.
	///
	/// O TERMOMETRO DA VISIBILIDADE E O `CharacterVisual`, e nao "algum filho": ele e a pilha do
	/// personagem (corpo, roupa, cabelo, contorno, rabo) e em jogo normal esta SEMPRE visivel. Aura,
	/// nuvem e sombra nao servem -- as tres passam a vida legitimamente apagadas, e uma delas
	/// apagada nao diz nada sobre o corpo ter sumido ou nao.
	/// </summary>
	private void OlharOMeuCorpo()
	{
		if (C is not { } cli || World.Instancia?.CorpoDeTeste(cli.LocalId) is not { } eu) return;
		if (eu.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;

		int quantos = 0;
		EsquivaZanzoken? efeito = null;
		foreach (Node f in eu.GetChildren())
			if (f is EsquivaZanzoken e) { quantos++; efeito = e; }

		// DONO UNICO: dois nodes no mesmo corpo e o desenho quebrado, e ele quebra do jeito pior --
		// o primeiro a terminar devolve o corpo por baixo do segundo, que segue desenhando listras.
		if (quantos > 1) _efeitoDuplicado++;

		if (efeito != null)
		{
			_quadrosComEfeito++;
			// A QUEIXA DO DONO, em numero: "o personagem q desviou deveria ficar INVISIVEL".
			if (vis.Visible) _corpoVisivelComEfeito++;
			if (efeito.GetInstanceId() != _idDoEfeito) { _idDoEfeito = efeito.GetInstanceId(); _trocasVistas++; }

			// E O QUE ESTA DESENHADO NO LUGAR: tem textura, e sai SEM TINTA. `Material` ou `Modulate`
			// diferente de branco sao as duas unicas formas de a arte preta chegar colorida na tela --
			// era exatamente um `ShaderMaterial` de silhueta azul que produzia a queixa de hoje.
			foreach (Node g in efeito.GetChildren())
			{
				if (g is not Sprite2D s) continue;
				if (s.Texture == null) _quadrosSemArte++;
				if (s.Material != null || s.Modulate != Colors.White || efeito.Modulate != Colors.White)
					_quadrosComTinta++;
			}
		}
		else
		{
			_quadrosSemEfeito++;
			// O VAZAMENTO. Sem efeito rodando nao ha desculpa nenhuma pro corpo estar apagado.
			if (!vis.Visible) _corpoInvisivelSemEfeito++;
			_idDoEfeito = 0;
		}
	}

	/// <summary>
	/// O PIXEL DA ARTE, medido da MESMA folha que o efeito carrega.
	///
	/// Nao e conferencia de catalogo: e `GetImage()` na textura do quadro e varredura pixel a pixel.
	/// Este porte ja "consertou" cor duas vezes lendo codigo em vez de arte e errou nas duas -- a
	/// unica prova que vale de "e preto" e o valor dos pixels opacos.
	/// </summary>
	private void MedirATinta()
	{
		var f = ResourceLoader.Load<SpriteFrames>("res://Assets/Sprites/Misc/Zanzoken.tres");
		if (f == null) { Conferir(false, "a folha do Zanzoken carregou"); return; }

		string nome = f.GetAnimationNames()[0];
		Conferir(f.GetFrameCount(nome) == 3, $"a folha tem os TRES quadros do `flick`: {f.GetFrameCount(nome)}");

		Image? img = f.GetFrameTexture(nome, 0)?.GetImage();
		if (img == null) { Conferir(false, "deu pra ler os pixels do quadro 0"); return; }

		int opacos = 0, pretos = 0;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.A < 0.5f) continue;
				opacos++;
				if (c.R < 0.02f && c.G < 0.02f && c.B < 0.02f) pretos++;
			}

		_passos.Add($"  --     arte do Zanzoken: {opacos} pixels opacos, {pretos} deles PRETOS puros");
		Conferir(opacos > 0 && pretos == opacos,
			$"a arte ja E preta no disco -- entao o conserto e NAO TINGIR, nao pintar: {pretos}/{opacos}");
	}

	/// <summary>
	/// QUEM NASCEU NA CAMADA `Atores` DESDE O QUADRO PASSADO.
	///
	/// E a resposta numerica pra "o efeito apareceu, e apareceu ONDE?" -- a foto responde pro olho,
	/// isto responde em pixels. Nao ha outro jeito: os efeitos sao nos avulsos que se auto-liberam,
	/// nao ha registro nenhum a consultar depois.
	/// </summary>
	private void VarrerOsNosNovos()
	{
		if (World.Instancia?.GetNodeOrNull<Node2D>("Atores") is not { } atores) return;

		var vivos = new HashSet<ulong>();
		foreach (Node n in atores.GetChildren())
		{
			vivos.Add(n.GetInstanceId());
			if (_conhecidos.Contains(n.GetInstanceId()) || _inspecionar <= 0) continue;
			if (n is not Node2D and not Control) continue;

			Vector2 onde = n switch
			{
				Node2D n2 => n2.Position,
				Control c2 => c2.Position + c2.Size / 2,   // o anel e um ColorRect posto pelo canto
				_ => Vector2.Zero,
			};
			_nosDaEsquiva.Add($"{n.GetType().Name,-16} em ({onde.X:0},{onde.Y:0})"
							  + $"  {Perto(onde)}");
		}
		_conhecidos.Clear();
		foreach (ulong id in vivos) _conhecidos.Add(id);
		if (_inspecionar > 0) _inspecionar--;
	}

	/// <summary>De quem esse ponto esta perto: de mim (quem desviou) ou de quem bateu.</summary>
	private string Perto(Vector2 onde)
	{
		if (C is not { } cli || World.Instancia is not { } m) return "";
		Vector2? eu = m.PosicaoDesenhadaDe(cli.LocalId);
		Vector2? ele = m.PosicaoDesenhadaDe(_idDoOutro);
		if (eu == null || ele == null) return "";
		float dEu = onde.DistanceTo(eu.Value), dEle = onde.DistanceTo(ele.Value);
		return $"-> {dEu:0} px de QUEM DESVIOU, {dEle:0} px de QUEM BATEU"
			   + (dEu < dEle ? "   (no defensor)" : "   (no atacante)");
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// Salva a tela. No headless o `GetImage` volta vazio -- e por isso esta bancada roda COM janela.
	///
	/// Sai tambem um recorte ampliado em volta do defensor: a tela inteira tem 1152 px e os dois
	/// corpos ocupam 64 deles, entao a foto cheia prova o ENQUADRAMENTO e o recorte prova o EFEITO.
	/// </summary>
	private bool Fotografar(string destino, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _passos.Add($"  --     {rotulo}: sem foto (headless nao renderiza)"); return false; }
		return Salvar(img, OndeEstouNaTela(), destino, rotulo);
	}

	/// <summary>
	/// GRAVA UMA IMAGEM JA TIRADA, com o recorte mirado onde o corpo estava QUANDO ELA FOI TIRADA.
	///
	/// A posicao vem de fora, e nao de `PosicaoDesenhadaDe` na hora de gravar, por causa da foto do
	/// "antes": ela e um quadro guardado, e mirar o recorte na posicao de AGORA amputaria justamente
	/// o corpo que essa foto existe pra mostrar.
	/// </summary>
	private bool Salvar(Image img, Vector2 naTela, string destino, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);

			// AS DUAS POSICOES, e nao so o vao: a primeira rodada com foto deu "vao 0 px" e ficou a
			// duvida de se os corpos estavam mesmo empilhados ou se a busca tinha devolvido o mesmo
			// no duas vezes. Dois pares de coordenadas respondem isso sem depender de olhar a foto.
			Vector2? eu = World.Instancia?.PosicaoDesenhadaDe(C?.LocalId ?? 0);
			Vector2? ele = World.Instancia?.PosicaoDesenhadaDe(_idDoOutro);
			_passos.Add($"  ok     {rotulo}: {caminho}  ({img.GetWidth()}x{img.GetHeight()})");
			_passos.Add($"           quem desviou (id {C?.LocalId}) em {eu}, quem bateu (id {_idDoOutro}) em {ele}"
						+ $" -- vao {Vao():0} px, altura {World.Instancia?.AlturaDeTeste ?? 0f:0} px");

			if (Recorte(img, naTela) is { } lupa)
			{
				string zoom = caminho.Replace(".png", "-zoom.png");
				lupa.SavePng(zoom);
				_passos.Add($"  ok     {rotulo}: recorte 3x em {zoom}");
			}
			return true;
		}
		catch (Exception e) { _passos.Add($"  --     {rotulo}: sem foto: {e.Message}"); return false; }
	}

	/// <summary>
	/// Um quadrado em volta do defensor, ampliado 3x sem suavizar.
	///
	/// A mira vem em pixels de TELA (ja passada pelo transform da camera pelo chamador): a camera
	/// segue o corpo local, mas nao exatamente -- ha suavizacao --, e um recorte cravado no centro da
	/// tela erraria o alvo justamente quando ela estivesse alcancando.
	/// </summary>
	private Image? Recorte(Image cheia, Vector2 tela)
	{
		if (tela == Vector2.Zero) return null;
		const int lado = 256;
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado);
		r = r.Intersection(new Rect2I(0, 0, cheia.GetWidth(), cheia.GetHeight()));
		if (r.Size.X < 32 || r.Size.Y < 32) return null;

		Image corte = cheia.GetRegion(r);
		corte.Resize(corte.GetWidth() * 3, corte.GetHeight() * 3, Image.Interpolation.Nearest);
		return corte;
	}

	// =====================================================================
	// O RELATORIO
	// =====================================================================
	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>
	/// TODO MUNDO ESTA VISIVEL NO FIM? -- a conferencia da TROCA LONGA, feita no ultimo quadro.
	///
	/// Os contadores de quadro olham o MEU corpo, que e o unico que a bancada alcanca sem adivinhar,
	/// e e o meu que esquiva. Mas o efeito e um node que qualquer corpo pode receber (o socador
	/// tambem esquiva quando quem bate sou eu, e a zona tem NPC brigando), e um corpo perdido no meio
	/// da troca nao daria sinal nenhum nos contadores -- so ficaria invisivel na tela, que e a
	/// queixa. Aqui a plateia inteira e conferida uma vez, quando nada mais deveria estar rodando.
	/// </summary>
	private void NinguemFicouInvisivel()
	{
		if (World.Instancia is not { } m) return;
		var apagados = new List<int>();
		int olhados = 0;
		foreach (int id in _todosOsIds)
		{
			if (m.CorpoDeTeste(id) is not { } corpo || !GodotObject.IsInstanceValid(corpo)) continue;
			if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) continue;
			olhados++;
			if (!vis.Visible) apagados.Add(id);
		}
		Conferir(apagados.Count == 0,
			$"depois da troca longa NINGUEM ficou invisivel: {olhados} corpo(s) olhado(s)"
			+ (apagados.Count > 0 ? $", APAGADOS: {string.Join(", ", apagados)}" : ""));
	}

	private const string Assobio = "meleeflash";
	private static readonly string[] SocoNoAr = ["meleemiss1", "meleemiss2", "meleemiss3"];

	/// <summary>
	/// O ARQUIVO E ESTE SOM? -- comparado so pelo NOME DO ARQUIVO, nunca pelo caminho.
	///
	/// ============================ A PASTA SE CHAMA "PUNCH EFFECTS" ============================
	/// Todo som de melee mora em `res://Assets/Sounds/Effects/Punch Effects/` (`Trilha.P`). Uma
	/// versao desta bancada procurou "punch" no caminho inteiro pra achar baque de impacto, e a
	/// PASTA respondeu por todos: 232 baques em 116 esquivas -- exatamente os dois sons corretos da
	/// esquiva (`meleeflash` + `meleemiss`) contados como pancada, uma reprovacao inventada em cima
	/// de um comportamento certo. O nome do arquivo e o unico pedaco que diz que som e aquele.
	/// =========================================================================================
	/// </summary>
	private static bool Eh(string arq, string nome)
		=> System.IO.Path.GetFileName(arq).Contains(nome, StringComparison.OrdinalIgnoreCase);
	private static bool EhSocoNoAr(string arq) => SocoNoAr.Any(n => Eh(arq, n));

	/// <summary>
	/// OS BAQUES: os sons que so quem APANHA ouve (`Trilha.Acerto`, a aparada, o corpo no chao).
	///
	/// ============================ POR QUE ESTA LISTA EXISTE ============================
	/// A conferencia "nenhum baque no quadro de uma esquiva" era escrita por exclusao -- QUALQUER som
	/// que nao fosse `meleeflash`/`meleemiss` contava como impacto. E reprovou uma esquiva perfeita: o
	/// que dividia o quadro com ela era o `chainswoop`, o rasgo do DASH de aproximacao
	/// (`Trilha.Dash`). O atacante chega investindo e soca no mesmo quadro em que chega -- isso e o
	/// combate funcionando, nao esquiva soando como pancada.
	///
	/// Por INCLUSAO a pergunta volta a ser a que importa: o jogador ouviu um som de CARNE quando o
	/// soco nao encostou nele? Som novo que nao esteja aqui passa despercebido, e e o risco aceito --
	/// o contrario (reprovar som legitimo) ja custou uma rodada inteira desta bancada.
	///
	/// E ELES SO VALEM CONTRA O NOME DO ARQUIVO -- ver <see cref="Eh"/>. Contra o caminho, "punch"
	/// casa com a PASTA de todos os sons de melee e a lista acusa cada esquiva de ser uma pancada.
	/// ==================================================================================
	/// </summary>
	private static readonly string[] Baques =
		["hit_", "punch", "kick", "groundhit", "parry", "perfectsound"];

	private static bool EhBaque(string arq) => Baques.Any(n => Eh(arq, n));

	private void Relatar()
	{
		var porDesfecho = _golpes.GroupBy(g => g.D).ToDictionary(g => g.Key, g => g.Count());
		int esquivas = porDesfecho.GetValueOrDefault(Jandirus.Core.Combat.Desfecho.Esquivou);
		int acertos = porDesfecho.GetValueOrDefault(Jandirus.Core.Combat.Desfecho.Acertou)
					+ porDesfecho.GetValueOrDefault(Jandirus.Core.Combat.Desfecho.Critico);

		_passos.Insert(0, "  --     golpes recebidos: "
			+ string.Join(", ", porDesfecho.Select(kv => $"{kv.Key} {kv.Value}")));

		// 1. A ESQUIVA ACONTECE. Sem isto nada mais importa -- e o desnivel de poder que a produz.
		Conferir(esquivas > 0, $"a esquiva ACONTECEU: {esquivas} de {_golpes.Count} golpes recebidos");
		Conferir(acertos > 0, $"e nem tudo foi esquiva: {acertos} acerto(s) pra comparar");

		// 2. O CORPO SOME E VOLTA. A pergunta cara -- ver o cabecalho.
		_passos.Add($"  --     quadros com o efeito no corpo: {_quadrosComEfeito}"
					+ $" | sem: {_quadrosSemEfeito} | trocas vistas comecarem: {_trocasVistas}");
		Conferir(_trocasVistas > 0,
			$"o efeito EXISTIU no corpo de quem desviou (nao so 'a funcao foi chamada'): {_trocasVistas} vez(es)");
		Conferir(_corpoVisivelComEfeito == 0,
			$"o corpo ficou INVISIVEL enquanto as listras rodavam: {_corpoVisivelComEfeito} quadro(s) com corpo aparecendo");
		Conferir(_corpoInvisivelSemEfeito == 0,
			$"a invisibilidade NAO VAZOU -- nenhum quadro com o corpo apagado e o efeito fora: {_corpoInvisivelSemEfeito}");
		Conferir(_efeitoDuplicado == 0,
			$"DONO UNICO por corpo -- nenhum quadro com dois nodes de esquiva empilhados: {_efeitoDuplicado}");

		// E O CORPO TEM QUE ESTAR DE VOLTA AGORA. Um efeito que morreu no meio (nocaute, troca de
		// zona, a cena caindo) so aparece aqui: os contadores de quadro nao veriam o ultimo estado.
		_passos.Add($"  --     o fim esperou {_esperandoQuieto:0.00} s por um quadro sem efeito nenhum"
					+ " (as duas conferencias abaixo perguntam pelo REPOUSO)");
		Conferir(MeuVisual() is { Visible: true }, "no fim da bancada o corpo esta VISIVEL de novo");
		NinguemFicouInvisivel();

		// 3. A TINTA -- o pixel da arte e o node que a desenha.
		MedirATinta();
		Conferir(_quadrosComTinta == 0,
			$"as listras saem SEM TINTA (nenhum Material, nenhum Modulate): {_quadrosComTinta} quadro(s) tingido(s)");
		Conferir(_quadrosSemArte == 0, $"as listras sempre tiveram textura: {_quadrosSemArte} quadro(s) vazio(s)");

		// 4. ONDE NASCERAM OS NOS DA CAMADA `Atores`. Depois da troca, o unico efeito da esquiva que
		// nasce ali e a FAISCA, e ela e do ATACANTE (`updateOverlay` em `src`, nao em `M`). Este bloco
		// deixou de ser prova do borrao -- ele e agora a prova de que a faisca nao migrou pro defensor.
		if (_nosDaEsquiva.Count > 0)
		{
			_passos.Add("  --     os nos que nasceram na camada Atores no quadro da esquiva:");
			foreach (string l in _nosDaEsquiva) _passos.Add("           " + l);
			_inspecionei = true;
		}
		Conferir(_inspecionei, "a faisca nasceu de verdade na camada de atores");

		// 5. O SOM. Cruza QUADRO a QUADRO -- e a unica leitura que separa "uma vez por esquiva" de
		//    "uma vez por quadro", que era exatamente a duvida do pedido.
		// DUAS ESQUIVAS NO MESMO QUADRO SAO DUAS ESQUIVAS, NAO UMA "DOBRADA". Os dois corpos trocam socos, e
		// desde que o servidor DESPEJA o fio no fim do tique (`TriggerUpdate`, a frente da fluidez) os dois
		// relatos de golpe resolvidos no mesmo tique chegam no MESMO quadro do cliente -- antes a grade de
		// 15 ms do LiteNetLib os espalhava por dois quadros e a coincidencia nunca aparecia. Os sons nao
		// tem dono; o que da pra exigir num quadro com N esquivas e N assobios e N socos no ar.
		int mudas = 0, dobradas = 0, certas = 0;
		var esquivasNoQuadro = _golpes.Where(g => g.Item3 == Jandirus.Core.Combat.Desfecho.Esquivou)
									  .GroupBy(g => g.Item1).ToDictionary(g => g.Key, g => g.Count());
		foreach ((ulong q, double t, Jandirus.Core.Combat.Desfecho d, bool _) in _golpes)
		{
			if (d != Jandirus.Core.Combat.Desfecho.Esquivou) continue;
			var noQuadro = _sons.Where(s => s.Quadro == q).ToList();
			int flash = noQuadro.Count(s => Eh(s.Arquivo, Assobio));
			int miss = noQuadro.Count(s => EhSocoNoAr(s.Arquivo));
			int juntas = esquivasNoQuadro[q];
			if (flash == 0 && miss == 0) mudas++;
			else if (flash == juntas && miss == juntas) certas++;
			else dobradas++;

			if (certas <= 3 && flash + miss > 0)
				_passos.Add($"  --     esquiva em t={t:0.00}s (quadro {q}): "
					+ string.Join(" + ", noQuadro.Select(s => $"{System.IO.Path.GetFileName(s.Arquivo)} vol {s.Vol:0.00}")));
		}
		Conferir(mudas == 0, $"nenhuma esquiva saiu MUDA: {mudas} de {esquivas}");
		Conferir(dobradas == 0, $"nenhuma esquiva soou dobrada nem pela metade: {dobradas} de {esquivas}");
		Conferir(certas == esquivas && esquivas > 0,
			$"cada esquiva tocou EXATAMENTE os dois sons do DM (meleeflash + meleemiss): {certas} de {esquivas}");

		// E O SOM NAO SAI SOZINHO. Um efeito por quadro apareceria aqui como som de melee em quadro
		// sem golpe nenhum -- e a diferenca entre "toca na esquiva" e "toca sempre".
		int soltos = _sons.Count(s => (Eh(s.Arquivo, Assobio) || EhSocoNoAr(s.Arquivo))
									  && !_quadrosComGolpe.Contains(s.Quadro));
		Conferir(soltos == 0,
			$"nenhum som de melee em quadro SEM golpe: {soltos} (seria som por quadro, nao por esquiva)");

		// O baque do impacto NAO pode entrar na esquiva -- esquivar nao soa como apanhar. Ver
		// `Baques`: a lista e por INCLUSAO, e nao "tudo que nao for assobio", que reprovava o dash.
		int baqueNaEsquiva = 0;
		foreach ((ulong q, _, Jandirus.Core.Combat.Desfecho d, bool _) in _golpes)
			if (d == Jandirus.Core.Combat.Desfecho.Esquivou)
				baqueNaEsquiva += _sons.Count(s => s.Quadro == q && EhBaque(s.Arquivo));
		Conferir(baqueNaEsquiva == 0, $"nenhum BAQUE de impacto no quadro de uma esquiva: {baqueNaEsquiva}");

		// 6. AS FOTOS -- os dois tripticos, o contraste e a troca longa.
		_passos.Add($"  --     menor vao ja visto entre os dois corpos: {_menorVao:0} px (a foto so sai abaixo de 96)");
		foreach (string l in _oQueODuranteViu) _passos.Add("  --     no instante do 2/3: " + l);

		Conferir(_tripticoNoChao, "saiu o TRIPTICO no chao (antes / durante / depois)");
		// O AR TEM UM PORTAO A MAIS: sem `--vooteste` o servidor recusa o voo e a fase aerea nao
		// acontece. O relatorio precisa dizer QUAL das duas coisas faltou -- "nao subiu" e "subiu e
		// nao esquivou" pedem consertos diferentes, e um "FALHA: sem triptico no ar" calaria os dois.
		Conferir(_alturaMaxima > 0f,
			$"os dois SUBIRAM (minha altura maxima: {_alturaMaxima:0} px, {_segundosNoAr:0} s fora do chao"
			+ " -- 0 significa voo recusado, falta `--vooteste`)");
		Conferir(_esquivasNoAr > 0, $"houve esquiva NO AR pra fotografar: {_esquivasNoAr}"
									+ " (zero com altura > 0 = o socador nao subiu junto, ver `--socarvoando`)");
		Conferir(_tripticoNoAr, "saiu o TRIPTICO no ar -- as listras nascem onde o corpo e DESENHADO, nao onde o no esta");
		Conferir(!_querDepois, "nenhum triptico ficou pela metade esperando o corpo voltar");

		Conferir(_fotosAcerto > 0, $"a foto de contraste (um ACERTO) saiu ({_fotosAcerto})");
		Conferir(_fotoDaTrocaLonga, "a foto DEPOIS DA TROCA LONGA saiu (a prova de vazamento pro olho)");

		GD.Print("\n[desvio] ===== BANCADA DO DESVIO =====");
		foreach (string l in _passos) GD.Print("[desvio] " + l);
		GD.Print($"[desvio] sons registrados no total: {_sons.Count} | golpes em mim: {_golpes.Count}"
				 + $" | quadros vistos: {Engine.GetProcessFrames()}");
		GD.Print(_falhas.Count == 0
			? "[desvio] ===== TUDO OK ====="
			: $"[desvio] ===== {_falhas.Count} FALHA(S) =====\n[desvio]   " + string.Join("\n[desvio]   ", _falhas));

		GetTree().Quit();
	}
}
