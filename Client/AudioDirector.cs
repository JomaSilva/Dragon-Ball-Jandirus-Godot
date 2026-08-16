using Godot;

namespace Jandirus.Client;

/// <summary>
/// O SOM DO JOGO: musica, ambiente e efeitos.
///
/// TRES BARRAMENTOS, criados em codigo (nao num .tres de layout, que e um arquivo binario
/// chato de versionar): Musica, Efeitos e Ambiente, todos filhos do Master. E o que permite
/// o menu de pause ter um controle de volume separado por tipo -- baixar a musica sem perder
/// o som dos golpes.
///
/// A MUSICA TEM PRIORIDADE, nao fila. Cada camada tem UM PEDIDO seu (ver <see cref="_pedidos"/>)
/// e quem toca e sempre o pedido da camada MAIS ALTA que tenha um. Uma transformacao interrompe o
/// tema de combate, que interrompe o tema do lugar -- o mesmo desenho do BYOND, onde a musica de
/// batalha abaixava pro tema de transformacao.
///
/// ============================ O SILENCIO E UM ESTADO, E NAO UM BURACO ============================
/// Toda maquina de musica nasce com um *"o que toco agora?"* que nunca sabe responder "NADA": a faixa
/// acaba, o `Finished` dispara, e o codigo sai procurando a proxima em vez de aceitar que acabou. E
/// por isso que o tema de MENU aparecia no meio do jogo em laco -- ele era o fundo, o padrao, o que
/// sobrava quando nenhuma camada reivindicava o tocador.
///
/// Aqui "nenhum pedido" e um estado LEGITIMO e que se sustenta sozinho: <see cref="Reavaliar"/> nao
/// varre nada procurando substituto, ele so olha os pedidos, e se nao houver nenhum ele CALA e fica
/// calado ate alguem pedir. Ninguem preenche o silencio por reflexo.
///
/// A regra do dono, literal: *"em combate so tocam as musicas de combate ATE A TAG DE COMBATE SAIR,
/// ai a musica simplesmente E PARADA. e em transformacao, no momento q ela acabar N TOCA MAIS NADA"*.
/// E a segunda frase dele, que decide o CRUZAMENTO das duas: *"se a musica de transformacao comecar
/// durante um combate (ELA TEM PREFERENCIA) ao terminar a musica de transformacao, VOLTA PRA MUSICA
/// DE COMBATE. acabou a tag, as musicas PARAM (a nao ser q abra o menu q tenha a musica do menu)"*.
/// Ver <see cref="AoTerminar"/>, que e o unico lugar que decide isso.
/// ==============================================================================================
///
/// Autoload: a musica nao pode parar quando a cena troca (login -> selecao -> mundo).
/// </summary>
public partial class AudioDirector : Node
{
    public static AudioDirector? Instance { get; private set; }

    public const string BusMusica = "Musica";
    public const string BusEfeitos = "Efeitos";
    public const string BusAmbiente = "Ambiente";

    /// <summary>
    /// A VOZ DAS OUTRAS PESSOAS. Barramento PROPRIO, e nao um canto do <see cref="BusEfeitos"/>.
    ///
    /// Duas razoes, e as duas sao do jogador: quem quer ouvir os socos e nao quer ouvir gente tem que
    /// poder baixar so um dos dois (e vice-versa, numa briga barulhenta), e voz e a unica coisa aqui
    /// que vem de outro ser humano -- misturar o controle dela com o do vento seria esconder o unico
    /// controle que alguem procura com pressa.
    /// </summary>
    public const string BusVoz = "Voz";

    /// <summary>
    /// O BARRAMENTO DO MEU MICROFONE -- e ele nao vai pro <c>Master</c>.
    ///
    /// ============================ ELE E UMA TOMADA, E NAO UM VOLUME ============================
    /// Os outros quatro existem pra SAIR som deles. Este existe pra ENTRAR: e nele que mora o
    /// `AudioEffectCapture` que o <see cref="Microfone"/> le. Por isso ele nasce MUDO -- no Godot o
    /// volume e o mute sao aplicados DEPOIS da cadeia de efeitos, entao a captura continua recebendo
    /// o sinal cheio enquanto nada disso chega ao alto-falante.
    ///
    /// Sem o mute, quem falasse ouviria a propria voz de volta com a latencia do buffer (~21 ms), que
    /// e exatamente o atraso que o ouvido percebe como eco e que trava quem esta falando.
    /// ========================================================================================
    /// </summary>
    public const string BusCaptura = "Captura";

    /// <summary>
    /// Quem manda quando duas musicas querem tocar. Maior vence.
    ///
    /// ============================ A `Raiva` NASCEU ENTRE `Combate` E `Transformacao`, E O LUGAR E O DM ============================
    /// O original tem tres canais de musica e a ordem entre eles esta escrita: `emit_TransformMusic`
    /// abre cortando o canal de raiva (*"a transformation always wins: cut any rage theme first"*,
    /// `BattleMusic.dm:133`) e `emit_RageMusic` desiste se um tema de forma estiver no ar
    /// (`:148`, `if(world.time &lt; M.transform_music_until) continue`); os dois chamam
    /// `duck_battle_music`, que cala o combate enquanto tocam.
    ///
    /// Essas TRES regras sao exatamente o que a comparacao numerica deste enum ja faz -- camada maior
    /// interrompe, camada menor so fica de sobreaviso (ver <see cref="Musica"/>). Nao houve canal novo,
    /// nem `if` novo: houve um valor no meio da fila.
    ///
    /// E POR ISSO O `Transformacao` MUDOU DE NUMERO. Ele e ordinal puro -- nao viaja na rede, nao vai
    /// pro save e nao e escrito em lugar nenhum como literal --, entao renumerar e de graca. Enfiar a
    /// raiva como `4` (acima da transformacao) custaria nada em compilacao e inverteria a unica regra
    /// que o DM escreveu duas vezes.
    /// ================================================================================================================
    /// </summary>
    public enum Camada { Lugar = 0, Menu = 1, Combate = 2, Raiva = 3, Transformacao = 4 }

    private AudioStreamPlayer _musicaA = null!, _musicaB = null!;   // dois: a troca e por fade
    private AudioStreamPlayer _ambiente = null!;
    private bool _usandoA = true;

    /// <summary>
    /// O QUE CADA CAMADA ESTA PEDINDO, indexado pelo <see cref="Camada"/>. Vazio = a camada nao
    /// quer nada. Toca sempre o pedido da camada mais alta que tenha um; se nenhuma tiver, silencio.
    ///
    /// ============================ ERA UMA STRING SO, E SEM DONO ============================
    /// Antes disto havia um unico `_faixaDeBaixo`: "a faixa que volta quando a de cima terminar".
    /// Uma string sem dono, escrita por qualquer camada e devolvida por qualquer motivo -- e era ela
    /// a musica de menu tocando em laco no meio da luta. O caminho era: ESC durante a briga estacionava
    /// o tema do menu ali (menu perde do combate), fechar o ESC nao o limpava (a camada que fechava nao
    /// era a que tocava), e quando a tag de combate caia a maquina "voltava" pra uma faixa de menu que
    /// ninguem mais queria -- em laco, porque a volta era sempre `repetir: true`.
    ///
    /// Com um pedido POR CAMADA, quem pediu e quem tira: fechar o menu apaga o pedido do menu e ponto.
    /// Nao existe mais uma faixa orfa esperando pra ser tocada por engano.
    /// ====================================================================================
    /// </summary>
    private readonly string[] _pedidos = ["", "", "", "", ""];

    private Camada _camadaAtual = Camada.Lugar;
    private string _faixaAtual = "";

    private double _fade;                    // 0..1 do cruzamento em andamento
    private const double DuracaoFade = 1.2;

    /// <summary>
    /// O DIARIO DA TRILHA (`--logtrilha`): uma linha por TROCA DE FAIXA, com o instante, o arquivo
    /// e o MOTIVO.
    ///
    /// ============================ MUSICA ERRADA NAO DEIXA RASTRO ============================
    /// A queixa do dono era *"comeca a tocar umas musicas do MENU em loop"* -- e nao havia como
    /// responder "quem mandou tocar isso?" senao adivinhando. Um efeito visual errado se fotografa;
    /// uma faixa errada so existe no ouvido de quem estava la, e quando ela troca de novo some a
    /// unica prova. Sem o motivo escrito NO INSTANTE da troca, o defeito de camada (uma camada
    /// devolvendo o comando pra outra) e indistinguivel de um arquivo trocado na pasta.
    ///
    /// So por bandeira, e nao sempre: sao ~40 linhas numa briga longa, e o console do jogo e o mesmo
    /// lugar onde o dono le os avisos que importam.
    /// ========================================================================================
    /// </summary>
    private static readonly bool Diario = Array.IndexOf(OS.GetCmdlineArgs(), "--logtrilha") >= 0;

    /// <summary>
    /// ESPIA DE BANCADA da MUSICA -- nula em jogo. Recebe (instante, camada, arquivo, motivo) de
    /// cada troca de faixa, inclusive as que resultam em SILENCIO (arquivo vazio).
    ///
    /// Irma da <see cref="Espiao"/> dos efeitos, e existe pelo mesmo motivo: a regra que o dono
    /// pediu ("quando a tag cai a musica PARA, e fica parada") e uma afirmacao sobre o que NAO
    /// acontece depois -- e nao ha foto de "nada assumiu o tocador nos 60 s seguintes". So a lista
    /// das trocas, com carimbo de tempo, responde isso.
    /// </summary>
    public static Action<double, Camada, string, string>? EspiaoDeMusica;

    /// <summary>
    /// QUE CAMADA MANDA AGORA, e QUE FAIXA esta no ar. So a bancada le -- ver `--diagforma`.
    ///
    /// ============================ POR QUE ELAS PRECISAM SER LIDAS DE FORA ============================
    /// A regra "a musica da transformacao alcanca o planeta inteiro" nao tem NADA na tela: ela e a
    /// diferenca entre um `if (_souEu)` existir ou nao existir no `Transformacao._Ready`. Quem roda o
    /// jogo sozinho -- que e como este port e testado -- ouve a faixa dos dois jeitos, porque ele e
    /// sempre o dono da cena. O defeito so aparece com duas pessoas no mesmo planeta, e nessa hora
    /// ninguem esta com o depurador aberto.
    ///
    /// E o comentario do `Rodar` AFIRMAVA por anos que a musica era so de quem virava, com o codigo
    /// fazendo o contrario. Um campo lido pela bancada e o que impede a proxima afirmacao dessas de
    /// sobreviver sem ninguem conferir.
    /// ============================================================================================
    /// </summary>
    public Camada CamadaDeTeste => _camadaAtual;

    /// <inheritdoc cref="CamadaDeTeste"/>
    public string FaixaDeTeste => _faixaAtual;

    /// <summary>
    /// TEM SOM SAINDO? Pergunta aos DOIS tocadores do Godot, e nao ao que esta maquina ACHA que
    /// decidiu. So a bancada le.
    ///
    /// ============================ O DIARIO REGISTRA A INTENCAO, NAO O SILENCIO ============================
    /// A checagem do "e continua parada" conta linhas do diario -- e o diario nasce em
    /// <see cref="Anotar"/>, ou seja, no instante em que a maquina DECIDE. Um defeito no
    /// <see cref="Calar"/> (parar so um dos dois tocadores, como era antes) nao escreve linha nenhuma:
    /// o relatorio diria "0 trocas depois do silencio" com uma faixa ainda tocando no ouvido do dono.
    ///
    /// Uniform escrito nao e pixel desenhado, e decisao anotada nao e ar parado. Duas perguntas
    /// diferentes precisam de duas fontes diferentes.
    /// ==================================================================================================
    /// </summary>
    public bool TocandoDeTeste => _musicaA.Playing || _musicaB.Playing;

    public override void _Ready()
    {
        Instance = this;
        CriarBarramentos();

        _musicaA = NovoPlayer(BusMusica);
        _musicaB = NovoPlayer(BusMusica);
        _ambiente = NovoPlayer(BusAmbiente);

        // um metodo NOMEADO por tocador (e nao um lambda) porque o `AoTerminar` precisa saber QUAL
        // dos dois acabou -- o sinal do Godot nao diz --, e porque lambda nao se cancela num `-=`
        _musicaA.Finished += AoTerminarA;
        _musicaB.Finished += AoTerminarB;
    }

    public override void _ExitTree() => Instance = null;

    private AudioStreamPlayer NovoPlayer(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus, VolumeDb = 0 };
        AddChild(p);
        return p;
    }

    /// <summary>
    /// Cria os barramentos se ainda nao existirem. Idempotente -- o autoload pode ser
    /// recarregado numa troca de cena e nao pode duplicar barramento.
    /// </summary>
    private static void CriarBarramentos()
    {
        foreach (string nome in new[] { BusMusica, BusEfeitos, BusAmbiente, BusVoz })
        {
            if (AudioServer.GetBusIndex(nome) >= 0) continue;
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, nome);
            AudioServer.SetBusSend(i, "Master");
        }

        // ============================ O DA CAPTURA E DIFERENTE, E POR ISSO SAI DO LACO ============================
        // Ele nao manda som pra lugar nenhum (fica no `Master` por obrigacao do motor, mas MUDO) e ele
        // carrega um efeito. Enfia-lo no laco acima o faria virar mais um barramento de saida -- e o
        // sintoma seria a propria voz voltando pro ouvido de quem fala, com 21 ms de atraso.
        // ======================================================================================================
        if (AudioServer.GetBusIndex(BusCaptura) < 0)
        {
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, BusCaptura);
            AudioServer.SetBusSend(i, "Master");
            AudioServer.SetBusMute(i, true);
            AudioServer.AddBusEffect(i, new AudioEffectCapture { BufferLength = 0.2f });
        }
    }

    /// <summary>
    /// O `AudioEffectCapture` do barramento do microfone, ou nulo se ele ainda nao existe.
    ///
    /// UM FUNIL SO porque quem procura efeito por indice numa lista que outra pessoa monta e quem
    /// acaba lendo o efeito errado. Ver <see cref="Microfone"/>.
    /// </summary>
    public static AudioEffectCapture? Captura()
    {
        int i = AudioServer.GetBusIndex(BusCaptura);
        if (i < 0) return null;
        for (int e = 0; e < AudioServer.GetBusEffectCount(i); e++)
            if (AudioServer.GetBusEffect(i, e) is AudioEffectCapture c) return c;
        return null;
    }

    // =====================================================================
    // VOLUME
    // =====================================================================
    public void AplicarVolumes(Settings s)
    {
        Volume("Master", s.VolumeGeral);
        Volume(BusMusica, s.VolumeMusica);
        // O VOLUME DE EFEITOS PASSA PELO VACUO, e nao direto pro barramento -- ver `Vacuo`. Chamar
        // `Volume(BusEfeitos, ...)` aqui era o defeito obvio: mexer no controle deslizante DENTRO do
        // espaco devolveria o som do soco no vacuo, e ninguem ligaria uma coisa a outra.
        _volEfeitos = s.VolumeEfeitos;
        AplicarEfeitos();
        Volume(BusAmbiente, s.VolumeAmbiente);
        Volume(BusVoz, s.VolumeVoz);
    }

    // =====================================================================
    // O SILENCIO DO ESPACO
    // =====================================================================

    /// <summary>
    /// ============================ NO VACUO NAO HA MEIO PRA O SOM ANDAR ============================
    /// Pedido do dono: *"no espaco o jogo n tem som, SOMENTE A MUSICA de combate. efeitos sonoros de
    /// ki, soco etc N EXISTEM, a nao ser q estejam DENTRO DE UMA NAVE como a capital ship. mas no
    /// espaco em si, usando roupa espacial ou sem ela, N TEM SOM, somente a OST"*.
    ///
    /// ============================ POR QUE NO BARRAMENTO, E NAO SOM A SOM ============================
    /// Porque calar chamada a chamada **deixaria buracos que ninguem veria**, e ha um medido: a
    /// `CargaVisual.LigarLaco` monta o `AudioStreamPlayer2D` do zumbido de carga NA MAO, com
    /// `Bus = BusEfeitos`, sem passar pelo <see cref="Efeito"/> nem pelo <see cref="EfeitoNoLugar"/>
    /// -- os dois funis por onde passam as outras 23 chamadas do mundo. Uma varredura por "onde toca
    /// som" acharia os dois funis e perderia esse terceiro, e o sintoma seria o zumbido do C tocando
    /// sozinho no vacuo, calado, sem nada que ligasse a causa ao efeito.
    ///
    /// No barramento sao TODOS de uma vez, inclusive o quarto que alguem escrever amanha sem ler isto.
    ///
    /// ============================ O QUE **NAO** E CALADO, E CADA UM TEM RAZAO PROPRIA ============================
    ///   * A MUSICA -- e o pedido literal (*"somente a OST"*). Ela vive no <see cref="BusMusica"/>,
    ///     entao ela sobrevive de graca: nao ha uma linha nova pra mante-la tocando. Calar o `Master`
    ///     a mataria junto, e e por isso que o corte e no barramento CERTO e nao no de cima.
    ///   * O AMBIENTE -- ja estava resolvido antes desta tarefa: o ramo do espaco em
    ///     `World.CarregarZona` pede `Ambiente("")` e sai antes da linha do vento.
    ///   * A VOZ -- barramento SEPARADO (<see cref="BusVoz"/>), e ela **continua tocando**. Ver a
    ///     nota de ressalva logo abaixo: e a unica escolha desta tarefa que nao e obviamente a que o
    ///     dono pediu, e por isso ela esta escrita e nao decidida no escuro.
    ///
    /// ============================ A NAVE-CAPITAL ESCAPA DE GRACA, E ISSO FOI CONFERIDO ============================
    /// *"a nao ser q estejam DENTRO DE UMA NAVE como a capital ship"* -- e **nenhuma linha foi escrita
    /// pra isso**. O interior e `ZoneKey.Interior("Nave", id)` (`Core/Tech/NaveGrande.cs`), ou seja
    /// `Kind == KindInterior` e nome `"Nave"`; o <see cref="Jandirus.Core.World.Espaco.EhEspaco"/>
    /// compara o nome com `"Espaco"` e devolve FALSO. Zona diferente, ramo diferente no
    /// `CarregarZona`, e o vacuo desliga sozinho ao entrar. Escrever uma excecao pra nave seria
    /// escrever um `if` que nunca e verdadeiro.
    ///
    /// ============================ E A ROUPA ESPACIAL NAO DEVOLVE O SOM ============================
    /// *"usando roupa espacial ou sem ela, N TEM SOM"*. Ela nao aparece nesta conta em lugar nenhum,
    /// e essa AUSENCIA e a regra -- o vacuo pergunta pela ZONA e mais nada. Fica escrito porque
    /// "quem tem traje ouve" e a suposicao natural de quem vier depois.
    /// ==========================================================================================================
    /// </summary>
    /// <param name="noVacuo">Estou no espaco aberto agora? Ver `Espaco.EhEspaco`.</param>
    public void Vacuo(bool noVacuo)
    {
        if (_vacuo == noVacuo) return;
        _vacuo = noVacuo;
        AplicarEfeitos();
    }

    /// <summary>Estou no vacuo agora? Pra bancada -- e a unica medida do estado deste corte.</summary>
    public bool NoVacuoDeTeste => _vacuo;

    /// <summary>
    /// O BARRAMENTO DE EFEITOS ESTA MUDO AGORA? Lido do `AudioServer` e nao do campo, de proposito:
    /// o campo diz o que a ultima chamada PEDIU e isto diz o que o mixer REALMENTE ficou. E a mesma
    /// distincao do `SpriteDeAura.CorNoMaterialDeTeste`, e ela existe pelo mesmo motivo -- uma
    /// bancada que le a propria intencao fica verde para sempre.
    /// </summary>
    public static bool EfeitosMudosDeTeste =>
        AudioServer.GetBusIndex(BusEfeitos) is var i && i >= 0 && AudioServer.IsBusMute(i);

    private bool _vacuo;
    private float _volEfeitos = 1f;

    /// <summary>
    /// AS DUAS RAZOES PRA O EFEITO CALAR, NUMA CONTA SO: o jogador zerou o controle, ou ele esta no
    /// vacuo. Um lugar decide -- e por isso mexer no volume dentro do espaco nao ressuscita o som, e
    /// sair do espaco nao devolve som pra quem tinha zerado o controle.
    /// </summary>
    private void AplicarEfeitos()
    {
        int i = AudioServer.GetBusIndex(BusEfeitos);
        if (i < 0) return;
        AudioServer.SetBusMute(i, _vacuo || _volEfeitos <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Clamp(_volEfeitos, 0.0001f, 1f)));
    }

    /// <summary>
    /// Volume de 0 a 1 -> decibeis. O ouvido e logaritmico: sem a conversao, metade do
    /// controle deslizante ja soaria quase no maximo. Zero vira MUDO de verdade, nao -80 dB.
    /// </summary>
    private static void Volume(string bus, float v)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, v <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Clamp(v, 0.0001f, 1f)));
    }

    // =====================================================================
    // MUSICA
    // =====================================================================
    /// <summary>
    /// A camada <paramref name="camada"/> passa a pedir esta faixa. Se ela for a mais alta com
    /// pedido, toca agora; se nao, fica guardada NA CAMADA DELA e entra quando as de cima sairem.
    ///
    /// CAMINHO VAZIO APAGA O PEDIDO, e isso e regra e nao conveniencia: `Trilha.MusicaDe(zona)`
    /// devolve "" pra toda zona sem tema, e a troca de zona precisa poder dizer "o lugar novo nao
    /// tem musica". Sair do Inferno com o `Demon World` ainda pendurado na camada `Lugar` e
    /// exatamente o tipo de faixa orfa que este arquivo existe pra nao ter mais.
    /// </summary>
    /// <param name="motivo">
    /// POR QUE esta camada esta pedindo isto, em uma frase. So vai pro diario (`--logtrilha`) e
    /// pra bancada -- ver <see cref="Diario"/>. E texto de quem chama porque so quem chama sabe:
    /// o `Musica(Trilha.Menu(), Menu)` do `Boot` e o do `PauseMenu` sao a MESMA chamada e dois
    /// acontecimentos diferentes, e era exatamente essa confusao que o dono estava ouvindo.
    /// </param>
    public void Musica(string caminho, Camada camada, string motivo = "pedido")
    {
        _pedidos[(int)camada] = caminho;
        Reavaliar(motivo: motivo);
    }

    /// <summary>Esta camada nao quer mais nada. Se era a que tocava, a maquina reavalia.</summary>
    /// <inheritdoc cref="Musica" path="/param[@name='motivo']"/>
    public void PararCamada(Camada camada, string motivo = "camada desistiu")
    {
        _pedidos[(int)camada] = "";
        Reavaliar(motivo: motivo);
    }

    /// <summary>
    /// NINGUEM QUER NADA: apaga todos os pedidos e cala. E o silencio pedido de PROPOSITO, e nao o
    /// silencio que sobra de uma faixa ter acabado.
    ///
    /// ============================ CENA CORTADA NO MEIO DEIXAVA PEDIDO ETERNO ============================
    /// O pedido de uma camada passageira so morre quando a FAIXA acaba (<see cref="AoTerminar"/>). Uma
    /// cinematica que e cortada antes disso -- o node liberado, o jogador desconectando, a bancada
    /// fechando a cena na mao -- nunca chega la, e o pedido dela fica de pe pra sempre calando todas as
    /// camadas de baixo.
    ///
    /// Foi assim que a bancada de forma achou o caso REAL: sair do servidor no meio de uma transformacao
    /// levava o pedido `Transformacao` pra tela de login, onde o `Musica(Trilha.Menu(), Menu)` do
    /// `VoltarAoLogin` perde de `Transformacao` -- e a tela de titulo ficava MUDA, sem nada na tela
    /// dizendo por que. Quem sai do mundo agora zera a maquina antes de pedir o tema do menu.
    /// ================================================================================================
    /// </summary>
    /// <inheritdoc cref="Musica" path="/param[@name='motivo']"/>
    public void Silenciar(string motivo = "zerado de proposito")
    {
        for (int i = 0; i < _pedidos.Length; i++) _pedidos[i] = "";
        Reavaliar(motivo: motivo);
    }

    /// <summary>
    /// O UNICO LUGAR QUE DECIDE O QUE TOCA. Olha os pedidos de cima pra baixo, pega o primeiro que
    /// existir -- e se nao existir nenhum, CALA. Nao ha lista de reserva, nao ha faixa padrao.
    /// </summary>
    /// <param name="forcar">
    /// Toca de novo mesmo que a decisao tenha dado a mesma faixa na mesma camada. So o encadeamento
    /// de combate usa isto: com uma pasta de uma faixa so, `Trilha.Combate()` devolve a mesma, e sem
    /// isto a briga ficaria MUDA por a maquina achar que ja estava tocando o que devia.
    /// </param>
    private void Reavaliar(bool forcar = false, string motivo = "")
    {
        Camada topo = Camada.Lugar;
        string faixa = "";
        for (int i = _pedidos.Length - 1; i >= 0; i--)
        {
            if (_pedidos[i].Length == 0) continue;
            topo = (Camada)i;
            faixa = _pedidos[i];
            break;
        }

        if (!forcar && faixa == _faixaAtual && topo == _camadaAtual) return;

        _camadaAtual = topo;
        _faixaAtual = faixa;

        Anotar(topo, faixa, motivo);

        if (faixa.Length == 0) { Calar(); return; }
        Cruzar(faixa, EmLaco(topo));
    }

    /// <summary>
    /// UMA LINHA POR TROCA. O silencio tambem e uma troca e tambem se escreve -- ele e metade do
    /// que o dono pediu, e uma linha que so aparece quando ALGO comeca nunca provaria que nada
    /// comecou. Ver <see cref="Diario"/>.
    /// </summary>
    private static void Anotar(Camada camada, string faixa, string motivo)
    {
        double t = Time.GetTicksMsec() / 1000.0;
        EspiaoDeMusica?.Invoke(t, camada, faixa, motivo);
        if (!Diario) return;

        if (faixa.Length == 0)
        {
            // NO SILENCIO NAO HA CAMADA, e escrever uma seria mentir. `Reavaliar` devolve `Lugar`
            // quando NENHUMA camada tem pedido -- e o valor de partida do laco, nao uma decisao --,
            // e o log lido pelo dono dizia `camada=Lugar` como se o tema do lugar estivesse tocando
            // um arquivo vazio.
            GD.Print($"[trilha] {t,8:0.00}s  {"SILENCIO",-46}  (nenhuma camada com pedido)  <- {motivo}");
            return;
        }

        GD.Print($"[trilha] {t,8:0.00}s  {faixa.GetFile(),-46}  camada={camada,-14} "
               + $"{(EmLaco(camada) ? "laco" : "uma vez")}  <- {motivo}");
    }

    /// <summary>
    /// CORTE SECO, os dois tocadores. O dono escreveu *"a musica simplesmente E PARADA"* -- nao ha
    /// esmaecer aqui porque nunca houve, e inventar um seria responder outra coisa. Parar os DOIS
    /// importa: no meio de um cruzamento ha faixa em cada tocador, e parar so o da frente deixaria a
    /// antiga subindo sozinha.
    /// </summary>
    private void Calar()
    {
        _musicaA.Stop();
        _musicaB.Stop();
        _fade = 1;   // nao ha cruzamento pendente pra mexer em volume de ninguem
    }

    /// <summary>
    /// QUEM TOCA EM LACO SAI DA CAMADA, e nao de um parametro do chamador.
    ///
    /// Sao dois tipos de pedido, e o laco e a diferenca entre eles: `Lugar` e `Menu` sao pedidos
    /// PERMANENTES -- valem enquanto o estado que os criou for verdade (estou nesta zona / o painel
    /// de pausa esta aberto) -- e por isso repetem. `Combate`, `Raiva` e `Transformacao` sao pedidos
    /// PASSAGEIROS: sao a musica de um acontecimento, tocam UMA vez e o que fazer quando acabam e a
    /// regra do dono (ver <see cref="AoTerminar"/>).
    ///
    /// Era um `bool repetir` que cada chamador passava, e um chamador so passava `false`. Duas pontas
    /// escolhendo a mesma coisa e o comeco de duas pontas discordando.
    /// </summary>
    private static bool EmLaco(Camada camada) => camada <= Camada.Menu;

    private void AoTerminarA() => AoTerminar(_musicaA);
    private void AoTerminarB() => AoTerminar(_musicaB);

    /// <summary>
    /// A FAIXA ACABOU SOZINHA. As regras do dono moram aqui, e so aqui.
    ///
    ///   * COMBATE: acabou com a tag de combate ainda de pe -> entra OUTRA de combate. Quem segura o
    ///     pedido da camada e o `World._lutaAte`; enquanto ele nao chamar `PararCamada(Combate)`, o
    ///     pedido existe e a luta continua com trilha. Quando a tag cai, o `PararCamada` apaga o
    ///     pedido e a maquina para -- nao troca por outra coisa, nao volta pro que tocava antes.
    ///
    ///   * TRANSFORMACAO (e RAIVA): acabou -> some com os pedidos passageiros ACIMA de `Combate`, e
    ///     SO com eles. Fora de combate nao ha nada embaixo e o resultado e o silencio que o dono
    ///     pediu (*"no momento q ela acabar N TOCA MAIS NADA"*); DENTRO de uma briga o pedido de
    ///     `Combate` continua de pe e o <see cref="Reavaliar"/> volta pra trilha de batalha sozinho.
    ///
    /// ============================ O CRUZAMENTO NAO E O MESMO CASO ============================
    /// Isto apagava o pedido de `Combate` junto, pela leitura literal do "N TOCA MAIS NADA" -- e quem
    /// se transformasse no meio de uma briga passava os ~88 s restantes da tag socando calado.
    /// Perguntei ao dono, e a resposta dele e a lei: *"se a musica de transformacao comecar durante um
    /// combate (ELA TEM PREFERENCIA) ao terminar a musica de transformacao, VOLTA PRA MUSICA DE
    /// COMBATE. acabou a tag, as musicas PARAM (a nao ser q abra o menu q tenha a musica do menu)"*.
    ///
    /// As duas situacoes parecem a mesma -- "a faixa da forma acabou" -- e nao sao, porque o que
    /// decide nao e a faixa que ACABOU, e o que ainda esta ACONTECENDO com o jogador. Fora de combate
    /// nao sobrou acontecimento nenhum, e ali vale inteira a razao de sempre: silencio que so dura ate
    /// a proxima faixa reivindicar o tocador nao e silencio, e uma pausa -- por isso esta maquina nunca
    /// sai procurando substituto. Dentro da briga o acontecimento NAO acabou: a tag esta de pe, os
    /// socos continuam, e o pedido de `Combate` nao e uma faixa interrompida esperando a vez -- e o
    /// estado atual do jogador, igualzinho a `Lugar` e a `Menu`. Quem apaga o pedido de combate e a
    /// TAG CAINDO (`World._Process` -> `PararCamada`), e mais ninguem.
    /// ======================================================================================
    ///
    /// OS PEDIDOS PERMANENTES SOBREVIVEM (`Lugar` e `Menu`), pela mesma razao. Apagar o `Lugar` aqui
    /// mataria o tema do Inferno pra sempre -- ele so e reescrito na troca de zona -- e o primeiro
    /// soco de uma briga la seria um silencio permanente que ninguem pediu. E o `Menu` e a segunda
    /// metade da frase do dono: o painel de ESC aberto e um estado, nao um acontecimento, entao a tag
    /// caindo com ele aberto cai NELE e nao no silencio.
    /// </summary>
    private void AoTerminar(AudioStreamPlayer quem)
    {
        // sobra de cruzamento: a faixa VELHA terminando enquanto a nova ja esta no ar nao decide nada
        if (quem != Atual()) return;

        Camada acabou = _camadaAtual;

        if (acabou == Camada.Combate)
        {
            _pedidos[(int)Camada.Combate] = Trilha.Combate();
            Reavaliar(forcar: true, motivo: "faixa de COMBATE acabou com a tag de pe -> encadeia outra");
            return;
        }

        // COMECA EM `Raiva`, E NAO EM `Combate`: e esta a linha inteira da regra do cruzamento. O laco
        // varre so as camadas ACIMA do combate, entao o pedido de `Combate` -- se houver -- sobrevive.
        for (int i = (int)Camada.Raiva; i < _pedidos.Length; i++) _pedidos[i] = "";
        Reavaliar(motivo: $"faixa de {acabou} acabou -> apaga os passageiros acima de Combate");
    }

    /// <summary>
    /// BANCADA: pula pro fim da faixa que esta tocando, deixando <paramref name="faltando"/>
    /// segundos. Devolve falso se nao havia nada tocando.
    ///
    /// ============================ POR QUE NAO SE FINGE O `Finished` ============================
    /// A regra inteira do dono depende do sinal `Finished` do Godot -- e ele so nasce de um tocador
    /// que chegou AO FIM do fluxo de verdade. Chamar `AoTerminar` na mao testaria o meu `if`, nao a
    /// maquina: nao passaria pelo `quem != Atual()` (a guarda que impede a faixa VELHA de um
    /// cruzamento decidir o jogo), nem provaria que os dois tocadores estao mesmo ligados ao sinal.
    ///
    /// As faixas de batalha tem 2 a 4 minutos. Adiantar o cabecote e a unica forma de ver o fim
    /// NATURAL de uma faixa quatro vezes seguidas sem o teste durar um quarto de hora.
    /// =======================================================================================
    /// </summary>
    public bool AdiantarParaOFimDeTeste(double faltando = 0.4)
    {
        AudioStreamPlayer p = Atual();
        if (!p.Playing || p.Stream == null) return false;

        double fim = p.Stream.GetLength();
        if (fim <= faltando) return false;
        p.Seek((float)(fim - faltando));
        return true;
    }

    private AudioStreamPlayer Atual() => _usandoA ? _musicaA : _musicaB;
    private AudioStreamPlayer Outro() => _usandoA ? _musicaB : _musicaA;

    /// <summary>
    /// AS FAIXAS JA CARREGADAS.
    ///
    /// ============================ MP3 NAO SE CARREGA DUAS VEZES ============================
    /// O importador do Godot le o arquivo INTEIRO pra memoria, de forma sincrona, na thread
    /// principal. As faixas de batalha tem 0,9 a 1,9 MB e a maior do menu tem 14,2 MB -- e o
    /// carregamento acontecia no PRIMEIRO SOCO de cada briga e a cada abertura do menu de pause.
    /// Um engasgo de quadro inteiro, no pior instante, toda vez.
    ///
    /// O cache troca isso por um custo unico. Ele so cresce com o que a sessao de fato tocou.
    /// =====================================================================================
    /// </summary>
    private readonly Dictionary<string, AudioStream> _faixas = [];

    private void Cruzar(string caminho, bool repetir)
    {
        if (!_faixas.TryGetValue(caminho, out AudioStream? fluxo))
        {
            fluxo = ResourceLoader.Load<AudioStream>(caminho);
            if (fluxo != null) _faixas[caminho] = fluxo;
        }
        if (fluxo == null) { GD.PushWarning($"[audio] faixa ausente: {caminho}"); return; }

        // o .import do Godot ja resolve o loop de ogg/mp3; wav depende do arquivo
        if (fluxo is AudioStreamOggVorbis ogg) ogg.Loop = repetir;
        else if (fluxo is AudioStreamMP3 mp3) mp3.Loop = repetir;

        AudioStreamPlayer novo = Outro();
        novo.Stream = fluxo;
        novo.VolumeDb = -60;
        novo.Play();

        _usandoA = !_usandoA;
        _fade = 0;
    }

    public override void _Process(double delta)
    {
        if (_fade >= 1) return;

        _fade = Math.Min(_fade + delta / DuracaoFade, 1);
        float t = (float)_fade;

        Atual().VolumeDb = Mathf.LinearToDb(Mathf.Max(t, 0.0001f));
        Outro().VolumeDb = Mathf.LinearToDb(Mathf.Max(1 - t, 0.0001f));
        if (_fade >= 1) Outro().Stop();
    }

    // =====================================================================
    // AMBIENTE
    // =====================================================================
    /// <summary>O fundo do lugar (vento, mar, cidade). Troca quando se muda de planeta.</summary>
    public void Ambiente(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho)) { _ambiente.Stop(); return; }

        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) { GD.PushWarning($"[audio] ambiente ausente: {caminho}"); return; }
        if (_ambiente.Stream == fluxo && _ambiente.Playing) return;

        if (fluxo is AudioStreamOggVorbis ogg) ogg.Loop = true;
        else if (fluxo is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;

        _ambiente.Stream = fluxo;
        _ambiente.Play();
    }

    // =====================================================================
    // EFEITOS
    // =====================================================================
    /// <summary>
    /// Um som pontual sem posicao (interface, aviso). Cria e descarta o player: som de UI e
    /// raro e nao vale um pool.
    /// </summary>
    public void Efeito(string caminho, float volume = 1f)
    {
        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) return;

        var p = new AudioStreamPlayer
        {
            Stream = fluxo,
            Bus = BusEfeitos,
            VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.0001f, 1f)),
        };
        AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>
    /// Som COM posicao no mundo -- so quem esta perto ouve, e o volume cai com a distancia.
    /// E o que faz um soco do outro lado do mapa nao estourar no ouvido de todo mundo.
    /// </summary>
    /// <summary>
    /// ESPIA DE BANCADA -- nulo em jogo. Recebe (arquivo, volume) de cada efeito POSICIONADO que
    /// realmente foi tocado, ja depois da carga do recurso.
    ///
    /// Existe porque som nao se fotografa: a unica prova de que a esquiva SOA -- e de que soa UMA
    /// vez por esquiva, e nao uma por quadro -- e contar as chamadas com carimbo de quadro e cruzar
    /// com os golpes do mesmo quadro. Fica DEPOIS do `ResourceLoader`, entao um arquivo ausente
    /// nunca vira "tocou" no relatorio.
    /// </summary>
    public static Action<string, float>? Espiao;

    public static void EfeitoNoLugar(Node2D onde, string caminho, float volume = 1f, float alcance = 480f)
    {
        if (string.IsNullOrEmpty(caminho)) return;
        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) { GD.PushWarning($"[audio] efeito ausente: {caminho}"); return; }
        Espiao?.Invoke(caminho, volume);

        var p = new AudioStreamPlayer2D
        {
            Stream = fluxo,
            Bus = BusEfeitos,
            MaxDistance = alcance,
            Attenuation = 1.5f,
            VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.0001f, 1f)),
        };
        onde.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }
}
