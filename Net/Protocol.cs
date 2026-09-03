using Jandirus.Core.Appearance;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// Contrato de rede. Serializacao escrita a mao de proposito: cada pacote destes vai voar
/// dezenas de vezes por segundo por jogador, e byte a mais aqui e banda desperdicada.
/// </summary>
public static class Protocol
{
    public const int DefaultPort = 7777;
    public const string ConnectionKey = "jandirus";
    public const int TickHz = 30;                 // passo do servidor
    public const float TickSeconds = 1f / TickHz;

    /// <summary>Canais do LiteNetLib. Estado que se repete vai por canal NAO confiavel: o proximo pacote conserta.</summary>
    public const byte ChannelReliable = 0;   // login, troca de zona, correcao
    public const byte ChannelState = 1;      // input e snapshot (sequenciado, sem reenvio)

    /// <summary>
    /// A VOZ, E SO ELA. Canal PROPRIO e nao confiavel -- ver <see cref="C2S.Voz"/>.
    ///
    /// ============================ POR QUE NAO CABIA NO <see cref="ChannelState"/> ============================
    /// Os dois sao nao confiaveis, entao a tentacao era reusar. Nao da: o `ChannelState` e
    /// **sequenciado**, e sequenciado quer dizer *"o pacote velho e descartado quando um novo ja passou"*.
    /// Isso e exatamente certo pro snapshot (a posicao de agora torna a de 33 ms atras inutil) e
    /// exatamente errado pra voz: dois quadros de voz seguidos sao dois PEDACOS DIFERENTES da mesma
    /// frase, e nenhum deles substitui o outro. Compartilhar o canal faria o snapshot comer silabas.
    /// ======================================================================================================
    ///
    /// **AS DUAS PONTAS TEM QUE SUBIR JUNTAS**: `ChannelsCount` e do `NetManager` e vale por conexao. Um
    /// servidor com 3 canais e um cliente com 2 nao negociam -- o pacote do canal 2 e simplesmente
    /// jogado fora, calado.
    /// </summary>
    public const byte ChannelVoz = 2;

    /// <summary>Quantos canais o `NetManager` das DUAS pontas abre. Ver <see cref="ChannelVoz"/>.</summary>
    public const byte TotalDeCanais = 3;

    /// <summary>
    /// A ENTRADA TEM TRES PASSOS, e nessa ordem: entrar na CONTA, escolher (ou criar) o
    /// PERSONAGEM, e so entao pisar no mundo.
    ///
    /// O modelo e o do Project Zomboid: nao ha conta global -- em cada servidor voce tem um
    /// perfil, e nele cabem tres personagens. Por isso a criacao acontece DEPOIS de conectar:
    /// so o servidor sabe quais slots ja estao ocupados.
    /// </summary>
    public enum C2S : byte
    {
        Login = 1,         // conta + senha
        InputState = 2,    // posicao que o CLIENTE calculou + direcao + se esta andando
        Ping = 3,
        Activity = 4,      // "estou treinando / meditando" -- quem paga o BP e o servidor
        Action = 5,        // golpear: tipo do golpe + zona mirada. Quem resolve e o servidor
        PickSlot = 6,      // "quero jogar com o personagem do slot N"
        CreateChar = 7,    // "o slot N esta vazio: cria este personagem nele"
        Guard = 8,         // guarda erguida / baixada
        Aim = 9,           // zona do corpo que estou mirando
        Lethal = 10,       // o `murderToggle`: lutar pra valer ou nao
        Chat = 11,         // falar: canal + texto. Quem decide quem ouve e o servidor
        Alvo = 12,         // "e nele que eu estou mirando" (0 = solta o alvo)
        Aprender = 13,     // "quero comprar esta skill" (typepath)
        Transformar = 14,  // subir (1) ou descer (0) a escada de transformacao
        Habilidade = 15,   // usar uma habilidade ativa, por id ("regenerar", ...)
        Cargo = 16,        // "" = me manda a lista de cargos; senao = reivindico este
        Tech = 17,         // tecnologia: comando + argumento ("construir", "Research_Station")
        Estilo = 18,       // trocar de estilo de luta ("" ou "-" = soltar a postura)
        Carregar = 19,     // segurando C: reunindo energia (1) ou soltou (0)
        Zanzoken = 20,     // "quero piscar pra este ponto" -- duplo clique no chao

        /// <summary>
        /// O CANAL DOS VERBS: comando + argumento, como o <see cref="Tech"/> ja fazia pra
        /// tecnologia.
        ///
        /// UM CANAL PRA TODOS de proposito. O original tinha 91 verbs so na aba "Other" e mais 90
        /// na de admin; um byte de opcode por verb encheria o enum e obrigaria a mexer no protocolo
        /// a cada verb novo. Aqui o verb novo e uma linha no `switch` do servidor e uma no registro
        /// do cliente -- o contrato de rede nao muda.
        ///
        /// QUEM AUTORIZA E O SERVIDOR, sempre. O cliente so esconde os botoes de admin de quem nao
        /// e admin; o `switch` do servidor confere de novo, porque esconder botao nao e permissao.
        /// </summary>
        Verbo = 21,

        /// <summary>
        /// A TECLA DO QUICK TIME EVENT do ZanzoClash: um byte com a letra que o jogador apertou.
        ///
        /// Vai CRU, sem "eu acertei": quem confere se a letra bate com a pedida e o servidor, que
        /// e quem sorteou. Um cliente que mandasse "acertei" venceria todo embate.
        /// </summary>
        ClashTecla = 22,

        /// <summary>
        /// APAGAR O PERSONAGEM DE UM SLOT: numero do slot + o NOME digitado a mao.
        ///
        /// ============================ POR QUE O NOME VIAJA JUNTO ============================
        /// Um "tem certeza? [sim]" nao protege nada: quem esta com o dedo no botao clica nos dois.
        /// Digitar o nome do personagem obriga a LER qual deles vai morrer -- e e a unica defesa
        /// que funciona contra apagar o slot errado, que aqui e uma perda que nao volta.
        ///
        /// A conferencia e do SERVIDOR e nao da tela. O cliente pode ate mostrar o campo, mas quem
        /// decide se o nome bate e quem tem o save na mao; senao bastaria mandar o pacote na mao
        /// pra pular a trava inteira.
        /// ===================================================================================
        /// </summary>
        DeleteChar = 23,

        /// <summary>
        /// UM QUADRO DE VOZ SAINDO DA MINHA BOCA: `ushort seq` + `byte n` + n bytes de Opus.
        ///
        /// ============================ NAO HA "PRA QUEM" NESTE PACOTE ============================
        /// E a metade mais importante do desenho. O cliente diz *"estou falando"* e nada mais -- quem
        /// ouve **e uma decisao que ele nunca toma**. Um campo de destino aqui (ainda que "so pra a
        /// zona") seria a porta pra um cliente modificado escolher a mesa ao lado, e nao ha conferencia
        /// no servidor que valha mais do que simplesmente nao existir o campo.
        /// ====================================================================================
        ///
        /// Vai no <see cref="ChannelVoz"/>, **nao confiavel**: quadro de voz perdido se JOGA FORA.
        /// Retransmitir voz e pior que perde-la -- o quadro atrasado chega depois do proximo e trava a
        /// fila inteira atras dele. O servidor recusa em silencio o que passar do teto (ver
        /// <see cref="Core.Social.VozLocal.Torneira"/>).
        /// </summary>
        Voz = 24,
    }

    /// <summary>
    /// QUANTO CABE NO ARGUMENTO DE UM VERB.
    ///
    /// Era 48, herdado do canal de tecnologia -- onde o argumento e um typepath curto. Mas o painel
    /// de admin manda TEXTO por aqui (o aviso ao servidor, a mensagem particular), e
    /// <c>NetDataReader.GetString(max)</c> NAO TRUNCA: acima do limite devolve string VAZIA.
    ///
    /// O efeito era pior do que um corte: o admin escrevia um aviso de sessenta letras, a caixa se
    /// limpava, o servidor lia "" e respondia "escreva o aviso antes". O texto sumia e a mensagem
    /// ainda dizia que ele nao tinha escrito nada.
    /// </summary>
    public const int MaxArgDeVerbo = 256;

    /// <summary>
    /// OS CANAIS DE FALA, com os MESMOS NUMEROS do `sayType()` do BYOND.
    ///
    /// Nao e capricho: o original tem seis modos e cada um com um alcance proprio, e manter a
    /// numeracao deixa a comparacao com `Talking.dm` direta -- quem for conferir se o alcance
    /// do sussurro esta certo abre o `if(2)` de la e o `Sussurro` daqui.
    ///
    /// <see cref="Sistema"/> nao existe no original: e o canal em que o proprio cliente escreve o
    /// que so interessa a quem esta jogando ("zona carregada", "sem Ki pro dash"). Fica fora da
    /// faixa 1-6 de proposito. Ele TAMBEM viaja, ao contrario do que dizia esta linha: o `Avisar`
    /// do servidor manda por ele, com autor vazio, o recado pessoal de uma pessoa so.
    ///
    /// ============================ O TEXTO NUNCA VEM COM O NOME DE QUEM FALA ============================
    /// O autor viaja no proprio pacote (`S2C.Chat` = canal + autor + texto) e quem monta a frase e o
    /// cliente -- "Fulano diz, '...'", "* Fulano faz X". Um `Falar(pl, Emote, $"{pl.Name} faz X")`
    /// sai na tela como "* Fulano Fulano faz X", e foi assim que quatro tecnicas do G4 ficaram
    /// (rasgo, ponto cronico x2, sugar vida). O balao sobre a cabeca deixou isso pior ainda: la o
    /// nome nao aparece nenhuma vez, porque o desenho ja aponta pra pessoa.
    ///
    /// Regra: `Emote` recebe o predicado SEM sujeito ("rasga o ar e desaparece dentro dele.").
    /// ==============================================================================================
    /// </summary>
    public enum Fala : byte
    {
        Ooc = 1,        // global, fora do personagem
        Sussurro = 2,   // 2 tiles ouvem o que foi dito; o resto so ve que alguem sussurrou
        Diz = 3,        // a fala normal. Com "!" vira grito e o alcance cresce
        Pensa = 4,      // "Fulano pensa consigo mesmo, '...'"
        Emote = 5,      // *Fulano faz alguma coisa*
        Looc = 6,       // fora do personagem, mas so pra quem esta perto
        Sistema = 200,  // so o cliente escreve; nunca sai na rede
    }

    /// <summary>
    /// As zonas do corpo que da pra mirar. A zona nao GARANTE o membro -- so pesa o sorteio a
    /// favor dele (ver <see cref="Jandirus.Core.Combat.Body.Sortear"/>).
    /// </summary>
    public static readonly string[] Zonas = ["", "cabeca", "torso", "abdomen", "bracos", "pernas"];

    public static string ZonaDe(byte i) => i < Zonas.Length ? Zonas[i] : "";

    /// <summary>
    /// A POSE que os outros veem. Cabe em 3 bits dentro do byte que ja carregava direcao e
    /// "andando" -- entao mostrar todo mundo socando e meditando custa ZERO byte a mais por
    /// jogador por tick.
    ///
    /// ============================ POR QUE `Nadando` E POSE, E NAO UM BIT ============================
    /// Nadar precisava viajar (quem esta do lado tem que ver a pose de voo e NAO ver sombra nenhuma),
    /// e o segundo byte de flags do <see cref="EntityState"/> fechou no `BitNaveGrande` -- o proprio
    /// comentario de la diz que espremer mais um seria errado. Aqui havia espaco de graca: o campo ja
    /// tem 3 bits e usava 0..5.
    ///
    /// E DIZER A COISA CERTA sai mais barato do que o bit diria: nadar **e** uma pose (o DM faz
    /// literalmente `icon_state = "Flight"`, `Swim.dm:17`), e nao um modificador de voo -- o corpo
    /// nao sobe, nao muda de andar e nao ganha altura no fio. Um bit ao lado de `Voando` teria
    /// convidado exatamente a leitura errada.
    ///
    /// O QUE ELA NAO E: a fonte do MODO DE TRAVESSIA. Socar nadando devolve `Atacando` (o ataque vem
    /// antes no `ServerPlayer.Pose`), e quem lesse o modo daqui veria o corpo "parar de nadar" a cada
    /// soco -- no meio do lago. Quem carrega o modo pro dono do corpo e o bit de nado do
    /// <see cref="SheetState"/>, que e continuo. Ver `SheetState.Nadando`.
    /// =============================================================================================
    ///
    /// ============================ E COM O `Canalizando` O CAMPO FECHOU ============================
    /// 3 bits sao 8 valores e o 7 era o ultimo. Quem precisar do NONO estado de corpo nao tem onde
    /// entrar, e a saida honesta e a mesma que o `BitNaveGrande` ja escreveu no outro byte: **um
    /// campo novo, opcional, pago so por quem o usa** -- exatamente como o <see cref="EntityState.Altitude"/>
    /// (so vai no fio com `Voando` ligado) e como o <see cref="EntityState.Canal"/> que este valor
    /// acabou de trazer (so vai no fio com esta pose de pe).
    /// ==========================================================================================
    /// </summary>
    public enum Pose : byte
    {
        Normal = 0, Treinando = 1, Meditando = 2, Atacando = 3, Voando = 4, Nocauteado = 5,
        Nadando = 6,

        /// <summary>
        /// ESTE CORPO ESTA COM UM CANAL DE KI DE PE -- reunindo energia pra um raio, ou com o raio
        /// saindo da mao.
        ///
        /// ============================ E POSE PORQUE E ESTADO, E NAO EVENTO ============================
        /// O pedido do dono foi literal: *"ao SOLTAR o sprite ficava na ANIMACAO DE SOCO pra DIRECAO
        /// q o beam esta sendo jogado, e ele so voltaria a posicao de IDLE quando ele PARASSE DE USAR
        /// O BEAM"*. Um raio sustentado dura segundos; a `Atacando`, que e um PRAZO
        /// (<see cref="AttackPoseMs"/>), acabaria sozinha e devolveria o corpo ao idle com o feixe
        /// ainda saindo da mao.
        ///
        /// E o DM diz a mesma coisa pelo avesso: `usr.icon_state = "Blast"` e escrito UMA vez, no
        /// instante do `beaming = 1` (`beams.dm:280` e as nove irmas), e a unica linha que o apaga e
        /// o `icon_state = ""` que ABRE o `stopbeaming()` (`beams.dm:212-213`). Nao ha prazo nenhum
        /// no meio -- a pose e a vida do canal.
        /// ===========================================================================================
        ///
        /// ============================ UM VALOR PRAS DUAS FASES, E A FASE VEM DE FORA ============================
        /// Carregar e atirar sao DESENHOS DIFERENTES no original -- carregando o corpo fica no idle e
        /// ganha um overlay (`addchargeoverlay()`, `beams.dm:300`), e so ao soltar e que o corpo muda
        /// de pose. Mas sao o MESMO ESTADO (o `CanalDeKi` do servidor, uma entrada so em `_canais`), e
        /// gastar os dois ultimos valores do enum pra dizer "o mesmo estado em duas fases" teria
        /// fechado o campo por uma diferenca que e de DESENHO.
        ///
        /// Entao a fase (e o desenho da carga) viajam no <see cref="EntityState.Canal"/>, que so
        /// existe no fio enquanto esta pose esta de pe. Ver la.
        /// ===================================================================================================
        /// </summary>
        Canalizando = 7,
    }

    /// <summary>
    /// OS DECALQUES DE CHAO -- marcas que o mundo ganha e perde. Ver `Client/Decalques.cs`.
    ///
    /// Sao SEIS coisas diferentes num sistema so porque, do ponto de vista do desenho, elas sao a
    /// MESMA coisa: um sprite no chao, atras dos corpos, com prazo de validade. Ter seis sistemas
    /// paralelos daria seis tetos de efeito pra calibrar e seis lugares pra esquecer de apagar.
    /// </summary>
    public enum Decal : byte
    {
        /// <summary>`craterseries` estado "crater": o sulco do meio do arrasto.</summary>
        Sulco = 0,
        /// <summary>`craterseries` estado "begin": as pontas do sulco (comeco e fim).</summary>
        SulcoPonta = 1,
        /// <summary>`Craters` estado "small crater": a cratera do fim, que CRESCE.</summary>
        Cratera = 2,
        /// <summary>`Dust Cloud 2018`: a fumaca que sobe da cratera.</summary>
        Fumaca = 3,
        /// <summary>`Damaged Ground`: terra revirada em volta do que caiu.</summary>
        ChaoDanificado = 4,
        /// <summary>`KiWater` (NS/EW): a agua se afastando de quem passa por cima.</summary>
        Agua = 5,
        /// <summary>`Blood spray`: respingo em volta de quem esta se acabando.</summary>
        Sangue = 6,
        /// <summary>
        /// `big crater`: a cratera GRANDE, das transformacoes que nascem da RAIVA.
        ///
        /// E outro TIPO e nao um parametro da <see cref="Cratera"/> porque tudo difere: a folha
        /// (96x96 contra o recorte `small_crater`), a escala final e o prazo. Ver `Decalques`.
        /// </summary>
        CrateraGrande = 7,
        /// <summary>
        /// `Body Parts Bloody`: O MEMBRO ARRANCADO, caido no chao.
        ///
        /// ============================ POR QUE ELE E UM DECALQUE ============================
        /// No BYOND a peca e um OBJETO de verdade (`/obj/bodyparts`, `mobparts.dm:328-477`): da pra
        /// pegar, largar e ate comer (`Eat`, `:383-393`, 20 de nutricao). Aqui ela e desenho, e a
        /// diferenca esta assumida: este porte nao tem item-no-chao nenhum -- `Core/Items` so tem
        /// inventario, e ate colher uma maca poe o item direto na mochila sem passar pelo mundo.
        /// Fazer o braco pegavel seria inventar um sistema de objeto solto com dono no servidor pra
        /// atender um caso; o pedido do dono foi "SPAWNAR NO CHAO o icon", que e o que isto faz.
        ///
        /// Se a peca precisar virar comida um dia, ela sai daqui e vira item -- e nao se acrescenta
        /// "pegavel" a um decalque, que e chao por definicao.
        /// ==================================================================================
        ///
        /// UNICO TIPO COM CARGA: leva um byte a mais no fio (`Core.Combat.PecaDeCorpo`),
        /// porque cabeca, braco e visceras sao recortes diferentes da MESMA folha. Ver
        /// `GameServer.MandarDecalque`.
        /// </summary>
        Membro = 8,
    }

    /// <summary>
    /// Quanto tempo a pose de soco fica no ar, quando o servidor ainda nao disse outra coisa.
    ///
    /// E so um DEFAULT de partida: a cadencia de verdade vem na ficha (<see cref="SheetState.SocoMs"/>),
    /// porque ela depende do `Eactspeed` -- carregar Ki deixa o personagem batendo mais rapido,
    /// e a animacao tem que acompanhar. 240 ms e a cadencia de um soco leve com stat inicial.
    /// </summary>
    public const int AttackPoseMs = 240;

    /// <summary>
    /// Bits do byte de input. Direcao ocupa 0-1; 0x40 e "estou correndo" e 0x80 e "estou
    /// andando". Correr e um PEDIDO: o servidor so concede enquanto houver Ki (ver
    /// `GameServer.Input`), entao afirmar aqui nao da velocidade de graca.
    /// </summary>
    public const byte InputCorrendo = 0x40;
    public const byte InputAndando = 0x80;

    /// <summary>
    /// SUBIR e DESCER voando -- dois dos quatro bits que sobravam neste byte.
    ///
    /// Vem no INPUT e nao pelo canal de habilidade porque altitude e continua: e "estou segurando
    /// espaco agora", igual a "estou andando", e nao um comando que acontece uma vez. Mandar por
    /// `Habilidade` obrigaria a inventar um "parei de subir" e a torcer pra ele nao se perder --
    /// aqui a ausencia do bit JA e o parar.
    /// </summary>
    public const byte InputSubir = 0x04;
    public const byte InputDescer = 0x08;

    /// <summary>Os golpes de melee. O `tipo` da conta de dano/cadencia sai daqui.</summary>
    public enum Golpe : byte { Leve = 0, Pesado = 1 }

    /// <summary>Peso de cada golpe na conta: soma no dano e multiplica a espera.</summary>
    public static double PesoDoGolpe(Golpe g) => g == Golpe.Pesado ? 3 : 1;

    /// <summary>
    /// O que o personagem esta fazendo. O cliente so DECLARA; quem roda a conta de ganho e
    /// paga o BP e o servidor -- senao bastaria mandar "estou treinando" mil vezes por
    /// segundo pra ficar forte.
    /// </summary>
    public enum Activity : byte { Parado = 0, Treinando = 1, Meditando = 2 }

    public enum S2C : byte
    {
        JoinAccepted = 1,  // id, zona (hash + descricao), seed pra geracao, ponto de spawn
        JoinRejected = 2,  // serve tambem pra recusar login e criacao de personagem
        SlotList = 10,     // os tres slots da conta, com o que a tela de selecao mostra
        Snapshot = 3,      // estado de quem esta na MESMA zona
        Correction = 4,    // "voce nao podia estar ai": posicao corrigida
        PeerLeft = 5,
        ZoneChanged = 6,
        Pong = 7,
        Stats = 8,         // a ficha viva: BP, BP expresso, Ki, vida, velocidade
        PeerLook = 9,      // a APARENCIA de alguem da zona -- mandada UMA vez, nao por tick
        Hit = 11,          // um golpe foi resolvido: quem, em quem, e no que deu
        Corpo = 12,        // o estado de CADA membro do meu personagem (so quando muda)
        Chat = 13,         // alguem falou e eu estou no alcance: canal + quem + o que
        Atributos = 14,    // a ficha LENTA: os 8 atributos e o que o menu abre com P mostra
        Skills = 15,       // o que eu aprendi, quantos marcos tenho, e o ESTADO das minhas arvores (ver PorEstadoDeSkills)
        Forma = 16,        // fulano mudou de forma: de, pra, e o byte de `Core.Forms.DegrauDeCena`
        Cargos = 17,       // a lista de cargos: chave, quem ocupa, e o que falta PRA MIM
        Vizinhanca = 18,   // os planetas por perto no espaco (so quando a CHUNK muda)
        Efeito = 19,       // caiu um efeito em mim: id + por quantos ms (0 = passou)
        Construcoes = 20,  // as construcoes de pe na minha zona
        Tech = 21,         // meu nivel de tecnologia, meu zeni e o catalogo com o motivo de cada nao
        Estilos = 22,      // meu estilo ativo, os que aprendi e a maestria de cada um
        /// <summary>
        /// FULANO SALTOU: id + DE ONDE ele saiu + um bool "deixa miragem?".
        ///
        /// SAO DUAS CAMADAS NO MESMO ANUNCIO, e elas tem donos diferentes:
        ///   * o BORRAO do deslocamento sai SEMPRE -- ele nao e tecnica, e so o corpo ter passado ali;
        ///   * a MIRAGEM (o vulto parado) so quando o bool vem ligado, porque ela e a Afterimage.
        ///
        /// O bool custa UM byte (13 -> 14) e existe pra que o borrao nao dependa de skill nenhuma --
        /// era essa dependencia que deixava o NPC sem borrao. Ver `GameServer.AnunciarZanzo`.
        /// </summary>
        Zanzo = 23,
        Porta = 24,        // porta abriu ou fechou (ou: a lista inteira, ao entrar na zona)
        /// <summary>
        /// UMA CELULA DO CENARIO CAIU: virou chao (knockback contra parede, ou o chao rachando).
        ///
        /// Carrega um `bool limpar` NA FRENTE das coordenadas. Ligado, ele quer dizer o oposto:
        /// "esqueca TODO o estrago desta zona" -- e o que o verb de admin que refaz o cenario
        /// precisa, e o mesmo truque do <see cref="Porta"/> com `completo = 1`. Sem esse bit o
        /// pacote so sabia dizer que algo caiu, e restaurar era impossivel de anunciar.
        /// </summary>
        Cenario = 25,

        /// <summary>
        /// AS CONTAS DO SERVIDOR, so pra quem e administrador.
        ///
        /// Existe porque promover alguem exige VER quem existe -- inclusive quem esta offline, que
        /// e o caso normal (o dono promove o amigo que jogou ontem). O alvo marcado por duplo
        /// clique, que serve pros outros verbs de admin, nao alcanca quem nao esta na tela.
        ///
        /// NAO CARREGA SENHA: nem o sal, nem o hash. So o que o painel desenha.
        /// </summary>
        Contas = 26,

        /// <summary>
        /// AS FERIDAS DE UM CORPO: cinco bytes dizendo quanto cada regiao esta marcada.
        ///
        /// ============================ POR QUE UM PACOTE PROPRIO ============================
        /// O <see cref="Corpo"/> ja carrega o estado de cada membro -- mas SO PRO DONO, porque
        /// membro alheio e informacao de combate (saber que o braco do outro esta quebrado vale
        /// uma luta). As feridas sao a parte VISIVEL disso: um corpo ensanguentado se ve de longe,
        /// e todo mundo tem que ver o MESMO corpo.
        ///
        /// Nao entra no snapshot: dano por membro muda quando alguem apanha, nao trinta vezes por
        /// segundo. Vai pelo caminho do <see cref="PeerLook"/> -- quando muda, e de novo pra quem
        /// chega na zona.
        ///
        /// CINCO BYTES, um por regiao (cabeca, torso, abdomen, bracos, pernas), cada um com dois
        /// nibbles: hematoma e sangue. Ver `Core.Combat.MascaraDeFeridas`.
        /// ==================================================================================
        /// </summary>
        Feridas = 27,

        /// <summary>
        /// O ZANZO CLASH: o duelo de velocidade que comeca quando dois lutadores com Imagem
        /// Remanescente se acertam no MESMO instante.
        ///
        /// Um opcode so pros cinco momentos (o `Sub` diz qual): comecou, tecla nova, placar,
        /// baque invisivel, acabou. Sao poucos bytes e acontecem em rajada dentro de quatro
        /// segundos -- cinco opcodes pra isso encheriam o enum sem separar nada.
        /// </summary>
        Clash = 28,

        /// <summary>
        /// A HORA DO MUNDO: UM `double` com os segundos decorridos desde a origem do tempo.
        ///
        /// ============================ POR QUE SO UM NUMERO ============================
        /// Nao vai aqui nem a hora do planeta, nem a fase da lua, nem se e noite. Tudo isso e
        /// FUNCAO PURA deste numero mais a ficha do planeta (`Core.World.Ceu`), e as duas pontas
        /// tem a ficha -- o cliente le o mesmo `planetas.json` que o servidor, e mundo gerado sai
        /// da seed. Mandar o resultado em vez do instante criaria uma segunda fonte pra hora, que
        /// e exatamente o que este pacote veio matar.
        ///
        /// De quebra, com o instante o cliente sabe que horas sao em QUALQUER planeta, nao so no
        /// que ele esta: a carta estelar consegue dizer "em Namek e dia" sem pedir nada.
        /// ==============================================================================
        ///
        /// O CICLO DO DIA ERA LOCAL ATE AQUI. O `Iluminacao` do cliente somava `delta` a partir
        /// de 0,42 a cada `_Ready` -- dois jogadores no mesmo planeta viam horas diferentes e
        /// quem entrava depois nunca alcancava. Enquanto o ceu era enfeite isso passava; com o
        /// Oozaru pendurado na lua cheia, viraria um virando macaco numa lua que o outro nao ve.
        /// </summary>
        Ceu = 29,

        /// <summary>
        /// UM CLIMA FORCADO nesta zona: tipo, ate quando (segundos do mundo), duracao e forca.
        ///
        /// ============================ SO O FORCADO VIAJA ============================
        /// O clima NATURAL nao vem por aqui, e nao precisa: ele e funcao pura da ficha do planeta
        /// mais o tempo do mundo, que o <see cref="Ceu"/> ja sincroniza. As duas pontas chegam a
        /// mesma chuva sem trocar um byte -- mesma jogada do terreno, da lua e do ceu de estrelas.
        ///
        /// O que NAO se deriva e alguem MANDANDO o ceu mudar: um SSJ3 escurecendo o mundo, um
        /// ritual de magia, um verb de admin. Isso e decisao, e decisao precisa ser contada.
        /// ============================================================================
        ///
        /// `tipo = 0` (Limpo) com `ate = 0` quer dizer "esqueca o que eu mandei" -- o ceu volta a
        /// ser o que o tempo disser. Sem esse caso, cancelar um clima forcado seria impossivel de
        /// anunciar e a zona ficaria presa na tempestade ate o prazo vencer.
        /// </summary>
        Clima = 30,

        /// <summary>
        /// UM DECALQUE NO CHAO: tipo + onde + em que direcao. Vai pra ZONA INTEIRA.
        ///
        /// ============================ POR QUE ISTO VEM DO SERVIDOR ============================
        /// A maioria dos decalques o cliente decide sozinho (chao danificado, agua, sangue) -- sao
        /// consequencia de coisas que ele ja ve. O RASTRO DE ARREMESSO nao: ele so existe se o
        /// arremesso todo tiver 8 tiles ou mais (regra do DU, `death.dm:218`), e a distancia TOTAL
        /// e uma coisa que so quem lancou o corpo sabe -- o cliente ve o corpo passar e nao tem como
        /// saber se ele vai parar no proximo tile ou dez adiante.
        ///
        /// E porque e da zona: quem assiste a briga tem que ver o sulco no chao, nao so quem voou.
        /// =====================================================================================
        /// </summary>
        Decalque = 33,

        /// <summary>
        /// CAIU UM RAIO, e ele caiu NUM LUGAR: posicao no mundo + a semente do desenho.
        ///
        /// ============================ POR QUE O RAIO E DO SERVIDOR ============================
        /// Ele era sorteado no cliente, e por isso era um efeito de tela: cada jogador via os
        /// proprios raios, em instantes diferentes, em lugares diferentes. Dois amigos lado a lado
        /// numa tempestade nao viam a MESMA tempestade -- e num jogo em rede isso e a diferenca
        /// entre um clima e um protetor de tela.
        ///
        /// Com a posicao vinda de fora, o raio vira acontecimento do MUNDO: quem esta com a camera
        /// naquele pedaco ve o risco cair; quem nao esta ve o clarao e ouve o trovao, e o trovao
        /// chega atrasado conforme a distancia. E o que da tamanho a tempestade -- saber que
        /// aquilo caiu longe, e onde.
        /// ======================================================================================
        ///
        /// A SEMENTE VIAJA JUNTO pra que o risco tenha a MESMA forma nas duas telas. Sem ela cada
        /// cliente desenharia um zigue-zague diferente pro mesmo raio, e quem estivesse do lado
        /// veria outro relampago.
        /// </summary>
        Raio = 31,

        /// <summary>
        /// O QUE EU CARREGO. So pro dono -- inventario alheio nao e do snapshot, e ver o que o
        /// outro tem na mochila e informacao que o jogo nao da de graca.
        ///
        /// A LISTA INTEIRA, e nao um delta. Sao no maximo 30 pares de (id, quantidade) e ela muda
        /// quando alguem colhe ou come -- nao vale um sistema de diferencas, que tem o modo de
        /// falha de dessincronizar em silencio e so aparecer como "sumiu um item".
        /// </summary>
        Inventario = 32,

        /// <summary>
        /// FULANO VIROU (OU DEIXOU DE SER) OOZARU: id, o byte de <c>Core.Forms.FormaOozaru</c>
        /// (0 nao, 1 regular, 2 dourado), se foi a PRIMEIRA vez daquele macaco, e o byte de
        /// <c>Core.Forms.DegrauDeCena</c>.
        ///
        /// Vai pra ZONA INTEIRA, como o <see cref="Forma"/>: ver alguem virar um macaco de dez
        /// metros e informacao que o mundo tem que ter, nao ficha pessoal.
        ///
        /// ============================ O DEGRAU, NUM PACOTE QUE E SEMPRE CENA CHEIA ============================
        /// A linha do macaco ignora maestria (`Cinematicas.Degrau`), entao o degrau aqui e sempre
        /// `Estreia`... enquanto o pacote for um ACONTECIMENTO. Ele tambem sai como ESTADO, unicast, pra
        /// quem acaba de entrar na zona (`GameServer.SincronizarFormas`) -- e ali vale `Nenhuma`, senao o
        /// recem-chegado ficaria preso assistindo a transformacao de alguem que virou macaco antes de ele
        /// pisar no planeta. Ver `World.AoVirarOozaru`.
        ///
        /// O `primeira` FICA AO LADO DELE porque diz outra coisa: e o carimbo `** Nome **` no chat do
        /// dono. A cena do macaco toca toda vez; o texto, so na estreia.
        /// ==================================================================================================
        ///
        /// ============================ POR QUE NAO E O `S2C.Forma` ============================
        /// A tentacao e obvia -- o Oozaru ate tem entrada no <c>Catalogo</c> (`oozaru`, ordem 10;
        /// `oozaru_dourado`, ordem 20) e o pacote de forma ja carrega (quem, de, pra, primeira).
        /// So que quem recebe `S2C.Forma` acaba em `Cinematicas.Para(def)`, e o macaco precisa de
        /// uma regra propria la dentro pra nao pegar a cena errada. (Ate a leva das 24 cenas novas
        /// aquela funcao tinha FALLBACK POR ORDEM e nunca devolvia null -- ordem 10 caia em `Ssj1`
        /// e ordem 20 em `Ssj2`, e o macaco gigante assistiria, paradinho, a cinematica de Super
        /// Saiyajin. Hoje o fallback nao existe mais, mas a regra da LINHA continua sendo o que
        /// mantem as duas entradas do Oozaru na cena do Oozaru.)
        ///
        /// E o Oozaru e um estado PARALELO no Core (ver o cabecalho de `Core/Forms/Oozaru.cs`:
        /// ele CONVIVE com o SSJ, o multiplicador sai de outro campo, e nao se sobe pra ele).
        /// Empurrar dois estados paralelos por um canal que so guarda um sempre acaba com um
        /// deles apagando o outro.
        ///
        /// O que se perdeu ao divergir: mais um opcode e mais um evento no cliente. O que se
        /// ganhou: a cena do Oozaru fica livre pra ser a que o dono pediu (aura base + tremor de
        /// camera, e NAO a `AuraBigCombined`), sem tocar em nada da escada SSJ.
        /// ===================================================================================
        /// </summary>
        Oozaru = 34,

        /// <summary>
        /// QUEM EU CONHECO -- a lista inteira, pessoal, so quando ela MUDA (ou quando eu peco).
        ///
        /// ============================ POR QUE A LISTA INTEIRA, E NAO O DELTA ============================
        /// Ela e pequena (as pessoas com quem alguem realmente convive, com nome e ficha curta) e
        /// muda devagar -- um passo de amizade a cada 3 s, e so quando ha alguem por perto. Um
        /// protocolo de delta aqui teria mais estado pra sincronizar do que dados pra mandar, e o
        /// primeiro relog fora de sincronia deixaria a aba mentindo sem ninguem notar.
        ///
        /// PESSOAL, sempre: quem voce conhece e a familiaridade que voce tem com cada um sao SEUS.
        /// Este e o unico pacote da rede que carrega assinatura de personagem -- e ela e OPACA (um
        /// numero de 10 digitos, como no DM), porque a tela a MOSTRA pra quem voce nao conhece.
        /// Ver `ServerPlayer.Assinatura` e `GameServer.MandarConhecidos`.
        /// ==============================================================================================
        /// </summary>
        Conhecidos = 35,

        /// <summary>
        /// A FURIA DE ALGUEM IRROMPEU: um id, e mais nada.
        ///
        /// ============================ UM PACOTE VAZIO, E ELE E VAZIO DE PROPOSITO ============================
        /// Nao vai grau, nao vai prazo, nao vai duracao de cena. O grau (`NivelDeRaiva`) e coisa do
        /// SERVIDOR -- e ele que le o gate das formas --, e a cena so existe pra UM deles (a furia
        /// EXTREMA, `Do_Anger_Stuff(1)`); mandar o grau daria ao cliente um numero que ele nao tem o
        /// que fazer com, e o primeiro a "usar" seria alguem escrevendo regra de jogo na tela.
        ///
        /// O prazo tambem nao: a cena mede 5,0 s no Core (`Cinematicas.Furia`) e as duas pontas leem o
        /// mesmo arquivo. Mandar a duracao criaria uma segunda verdade sobre o relogio dela.
        ///
        /// ============================ PRA ZONA INTEIRA, COMO O <see cref="Forma"/> E O <see cref="Oozaru"/> ============================
        /// No DM a cena e do MUNDO: as ondas de choque sao objetos no chao, o `to_chat` vai pra
        /// `view(src)` e o `emit_RageMusic` toca pra todo `mob` do `view`. Ver alguem explodir de raiva
        /// e informacao que quem esta em volta tem que ter -- e o mesmo argumento que ja tirou o Oozaru
        /// do caminho pessoal.
        ///
        /// ============================ E POR QUE NAO E O <see cref="Efeito"/> ============================
        /// A tentacao era obvia: aquele pacote ja carrega "caiu um efeito em mim: id + por quantos ms".
        /// So que ele e PESSOAL (um buff no meu corpo, com prazo) e a furia e um ACONTECIMENTO da zona,
        /// sem estado a sincronizar -- quem chega depois nao deve assistir a uma erupcao que aconteceu
        /// antes de ele pisar ali. Empurrar um acontecimento por um canal de estado e o que fez o
        /// Oozaru precisar de opcode proprio, e pelo mesmo motivo.
        /// =====================================================================================================
        /// </summary>
        Furia = 36,

        /// <summary>
        /// UM ATAQUE DE KI NASCEU OU MORREU: o ACONTECIMENTO, nao o estado.
        ///
        /// ============================ POR QUE DOIS CANAIS PRO MESMO TIRO ============================
        /// A POSICAO do projetil viaja no <see cref="Snapshot"/>, num segundo bloco depois dos corpos
        /// (ver <see cref="ProjetilState"/>) -- e o lugar certo: e estado continuo, 30 Hz, sequenced,
        /// um buffer por zona, e um pacote perdido custa um quadro de posicao velha e nada mais.
        ///
        /// Mas NASCER e MORRER nao sao estado: sao os dois instantes em que ha efeito pra tocar (o
        /// fogo saindo da mao, o estouro na cara de quem levou) e a unica hora em que o tipo, a cor e
        /// o dono precisam ser ditos. Perder isso num canal sem garantia deixa o projetil aparecer do
        /// nada no meio do caminho e sumir sem estourar -- e o cliente nao teria como saber que
        /// perdeu. Entao os dois eventos vao no canal confiavel, e so eles.
        ///
        /// E A MESMA DIVISAO DO <see cref="Zanzo"/>/<see cref="Clash"/>: evento confiavel, estado
        /// barato. O `Sub` diz qual dos dois -- ver <see cref="ProjetilSub"/>.
        /// ===========================================================================================
        /// </summary>
        Projetil = 37,

        /// <summary>
        /// AS TECNICAS DE KI QUE EU INVENTEI, e a MESA aberta (se houver).
        ///
        /// ============================ UM PACOTE SO PRA AS DUAS COISAS ============================
        /// A tela de montagem precisa das duas ao mesmo tempo -- a lista (pra escolher qual editar,
        /// e pra saber quantos slots restam) e o rascunho em edicao com os pontos ja gastos. Manda-las
        /// em dois pacotes deixaria um quadro em que a tela mostra a mesa nova ao lado da contagem
        /// velha, e o numero de pontos e justamente o que o jogador esta olhando quando clica.
        ///
        /// E ELE E A UNICA FONTE DOS PONTOS. O cliente NAO recalcula custo nenhum: aperta o botao,
        /// o servidor aplica a regra do `Core` e devolve a mesa inteira. Uma segunda copia da tabela
        /// de precos no cliente e a regra 4 da casa sendo violada -- e a tabela tem dezoito linhas.
        /// ====================================================================================
        /// </summary>
        Customizadas = 38,

        /// <summary>
        /// OS PLANETAS MORTOS -- a lista inteira, sempre que ela muda (e no login).
        ///
        /// ============================ POR QUE ELE PRECISA EXISTIR ============================
        /// O cliente **enumera planetas sozinho**: a carta estelar chama `Espaco.PreFeitos()` e
        /// `Sistemas.Do` direto, e desenha o que esta a anos-luz da vizinhanca ativa. Um bit no
        /// <see cref="Vizinhanca"/> (que so fala dos planetas por perto) nao alcancaria a carta --
        /// ela poria "Viajar" em cima de um planeta que ja virou po.
        ///
        /// **LISTA INTEIRA, e nao delta.** Ela e pequena por construcao (so entra o que alguem
        /// matou: duas ou tres entradas num servidor com as sagas consumadas), e um delta exigiria
        /// que as duas pontas concordassem sobre o que ja foi mandado -- estado a mais, pra
        /// economizar bytes que nao existem.
        ///
        /// Formato: `byte n`, e por entrada `string chave` + `string nome` + `byte fase` +
        /// `byte estagio` + `double faltam`. A CHAVE e a identidade (nome de pre-feito, ou "#seed" de
        /// procedural) -- ver `Core.World.ChaveDePlaneta`, e por que o nome sozinho nao serve.
        ///
        /// ============================ O `faltam` E O QUE DESENHA A AGONIA ============================
        /// Sao os segundos que restam do passo atual. Sem ele o cliente sabia que um planeta esta
        /// explodindo e nao sabia HA QUANTO TEMPO -- e a crosta de magma, as rachaduras e a explosao
        /// final que o dono pediu sao todas funcao desse numero. Ele nao dava pra integrar do lado de
        /// la: este pacote so sai quando algo MUDA DE ESTAGIO, e os cinco minutos da explosao sao um
        /// estagio so. Ver `GameServer.MandarMortos`.
        /// ================================================================================
        /// </summary>
        Mortos = 39,

        /// <summary>
        /// ONDE ESTA A MINHA NAVE, na coordenada da GALAXIA -- o "Observar" da ponte da Capital Ship.
        ///
        /// ============================ POR QUE UM PACOTE, E NAO UMA LINHA DE CHAT ============================
        /// O texto o `Chat` ja daria. O que ele nao da e a CARTA ESTELAR se centrar na nave: dentro de
        /// uma sala de 100x100 sem janela, `MapaEstelar.MinhaPosicaoNaGalaxia()` nao tem o que
        /// responder -- a zona do interior nao fica em lugar nenhum do universo, e o jogador ficaria
        /// olhando pra o ultimo lugar de onde ele desceu, que pode ser outra galaxia.
        ///
        /// No DM isso era `client.eye = ship`, uma camera. Este port nao tem olho remoto (o corte de
        /// interesse e por ZONA), e a carta estelar responde melhor a mesma pergunta -- ver
        /// `GameServer.NaveGrande.ObservarDaPonte`.
        ///
        /// Formato: `float x` + `float y` + `string zona` + `float cascoPct`.
        ///
        /// NAO HA PACOTE DE "APAGAR", e nao deve haver: quem apaga a marcacao e o proprio cliente,
        /// quando a zona dele deixa de ser o interior de uma nave (ver `World.OnZoneChanged`). Ele
        /// sabe disso sozinho e sabe na hora; um segundo caminho pelo servidor seria uma verdade
        /// duplicada que um dia chega atrasada -- e o sintoma seria a carta apontando pra um casco
        /// que ficou pra tras.
        /// ================================================================================================
        /// </summary>
        Nave = 40,

        /// <summary>
        /// A PREVIA DA LIMPEZA TOTAL DO SERVIDOR -- o primeiro dos dois passos do verb que apaga tudo.
        ///
        /// ============================ POR QUE A PREVIA E UM PACOTE, E NAO LINHAS DE CHAT ============================
        /// O que este pacote carrega nao e informacao: e o SEGUNDO PASSO da confirmacao. Ele traz um
        /// codigo sorteado na hora, que so existe no servidor e so vale por um minuto -- e sem ele o
        /// verb que apaga o mundo nao roda. Isso torna impossivel: apagar o servidor com um clique,
        /// apagar por um botao que o mouse encostou, e apagar com um comando decorado de antes
        /// (o codigo de ontem nao vale hoje).
        ///
        /// Pelo chat isso seria fragil de um jeito bobo: a linha rola pra cima com qualquer conversa,
        /// e o admin confirmaria de memoria -- ou pior, copiaria do log um codigo velho. Num pacote, o
        /// painel desenha a lista, o codigo e o campo de digitacao no mesmo lugar, e o codigo morre
        /// junto com a tela.
        ///
        /// AS LINHAS SAO O INVENTARIO: uma por sistema do mundo, com a CONTAGEM ("858 conta(s)",
        /// "12 construcao(oes) de pe"). Quem confirma tem que saber o tamanho do que esta fazendo --
        /// e o tamanho e um numero, nao um adjetivo.
        /// ========================================================================================================
        ///
        /// Formato: `string codigo` + `ushort segundosDeValidade` + `byte n` + n x `string linha`.
        /// Codigo vazio quer dizer "a previa venceu ou foi consumida" -- e o que fecha o painel.
        /// </summary>
        Limpeza = 41,

        /// <summary>
        /// QUAL NAVE ESTA EMBAIXO DE MIM -- o alvo da tecla E quando nao ha nada no chao pra apertar.
        ///
        /// ============================ POR QUE O SNAPSHOT NAO RESPONDE ISTO ============================
        /// Ele quase responde: <see cref="EntityState.Pilotando"/> diz que ha uma nave, e
        /// <see cref="EntityState.NaveGrande"/> diz se ela e a grande. Dois bits, e eles bastavam pra
        /// escolher o SPRITE, que era pra que nasceram. Nao bastam pra montar um MENU: o menu do pod
        /// oferece "Melhorar velocidade" e o do foguete oferece "Recondicionar", e os dois bits nao
        /// distinguem pod de foguete. E nao ha um terceiro bit: o segundo byte de flags fechou com o
        /// `BitNaveGrande` (ver o comentario de la).
        ///
        /// ALARGAR O SNAPSHOT SERIA O CANAL ERRADO de qualquer jeito. Ele sai 30 vezes por segundo
        /// POR CORPO, e "em que veiculo eu estou" muda uma vez por embarque e so interessa a UMA
        /// pessoa -- a que embarcou. E o mesmo argumento que o proprio `BitNaveGrande` escreveu pra
        /// nao mandar o caminho da arte, invertido: aquilo todo mundo precisa ver, isto nao.
        ///
        /// Entao ele e pessoal e por evento, igual ao <see cref="Nave"/> logo acima: sai no embarque,
        /// no desembarque, ao assumir e ao largar o leme, e quando a nave deixa de existir.
        /// ==========================================================================================
        ///
        /// Formato: `string tipo` + `string nome`. O tipo e o id do catalogo ("Spacepod",
        /// "Rocket_Ship", "Capital_Ship") e e ele que escolhe as acoes do menu; o nome e o que o
        /// menu ESCREVE, e vem junto porque o veiculo nao esta em lista nenhuma do cliente -- ele
        /// saiu do pacote de construcoes ao ser pilotado.
        ///
        /// **TIPO VAZIO QUER DIZER "NENHUM"**, e por isso este pacote tem o "apagar" que o
        /// <see cref="Nave"/> nao tem: aqui quem desembarca continua na mesma zona (o pod fica aos
        /// seus pes), entao o cliente nao tem como saber sozinho que deixou de estar a bordo.
        /// </summary>
        Veiculo = 42,

        /// <summary>
        /// OS CHEFES QUE EU JA VI, e portanto posso reenfrentar dentro da propria mente.
        ///
        /// ============================ POR QUE O NOME VIAJA JUNTO DO ID ============================
        /// Porque o cliente **nao le o `npcs.json`**. Ele conhece o que pode apertar, e conhece pelo
        /// pacote -- exatamente o argumento que o <see cref="Tech"/> ja escreveu pro catalogo de
        /// obras ("a arte vem do servidor porque o cliente nao le `construcoes.json`") e que o
        /// <see cref="Veiculo"/> repetiu pro nome do veiculo. Mandar so o id faria o menu oferecer
        /// "enfrentar freeza_namek".
        ///
        /// LISTA INTEIRA E NAO INCREMENTO, como os <see cref="Estilos"/> e as
        /// <see cref="Customizadas"/>: ela tem poucas entradas, cresce uma vez por chefe na vida do
        /// personagem e um "acrescente este" exigiria que as duas pontas nunca perdessem um pacote.
        /// Sai no login e a cada chefe novo anotado.
        /// ====================================================================================
        ///
        /// Formato: `byte n` + n x (`string molde`, `string nome`). O molde volta ao servidor no
        /// canal de habilidade como `mente_chefe:&lt;molde&gt;`.
        /// </summary>
        MenteChefes = 43,

        /// <summary>
        /// UM QUADRO DE VOZ DE ALGUEM QUE EU TENHO DIREITO DE OUVIR.
        ///
        /// ============================ ELE SO EXISTE PORQUE O SERVIDOR JA CORTOU ============================
        /// Este pacote **nunca sai** pra quem esta longe, em outra zona, ou pra quem nao cabe nas quatro
        /// vozes mais proximas (<see cref="Core.Social.VozLocal.MaxFalantesPorOuvinte"/>). Nao ha campo
        /// "ignore isto": quem nao devia ouvir nao recebe o byte. Ver o cabecalho de `VozLocal`.
        /// ================================================================================================
        ///
        /// Formato: `int falante` + `ushort seq` + `byte distancia` + `byte parede` + `byte n` + n bytes
        /// de payload Opus. **49 B tipicos**, + 28 B de UDP/IP = 77 B por quadro, 50x por segundo.
        ///
        ///   * `seq` -- numero do quadro NA ORIGEM. O canal e sequenciado e nao confiavel, entao chega
        ///     buraco: e o `seq` que diz ao decodificador que houve perda (o Opus preenche o vao) em
        ///     vez de ele emendar duas metades de silabas diferentes.
        ///   * `distancia` -- 0..255 sobre o alcance da fala. **Nao e redundante com a posicao do corpo**:
        ///     quem voa alto some da TELA de quem esta no chao (`Voo.Enxerga`), e ai o ouvinte tem a
        ///     voz e nao tem o corpo. (Do SNAPSHOT ele nao some: o corte da vista e do cliente, o
        ///     buffer da zona vai igual pra todo mundo.) Ver `VozOuvida.Tocar`.
        ///   * `parede` -- 1 quando ha parede no meio. **Quem responde isso e o servidor**, consultando o
        ///     MESMO bitset que cega a vista (`.vis`). O cliente decide como aquilo SOA; ele nao decide
        ///     se ha parede, senao a voz e a vista discordariam sobre o que e parede.
        /// </summary>
        Voz = 44,

        /// <summary>
        /// ESTE CORPO ESTA MORTO -- e o desenho disso e a AUREOLA sobre a cabeca.
        ///
        /// Formato: `int id` + `bool tem`. Dois campos, cinco bytes.
        ///
        /// ============================ POR QUE NAO E UM BIT DO SNAPSHOT ============================
        /// Os dois bytes de flags do <see cref="EntityState"/> estao CHEIOS -- o primeiro com direcao
        /// (2 bits), pose (3) e rabo/oculto/andando; o segundo com os oito de carga, sobrecarga,
        /// deitado, correndo, voando, sem-redeas, pilotando e nave grande. Um terceiro byte custaria
        /// um byte por corpo por tique (com 151 habitantes na zona, ~4,5 KB/s) pra carregar UM bit
        /// que muda **duas vezes por vida de personagem**.
        ///
        /// Entao ela vai pelo canal do estado LENTO, que este port ja tem em duas cores -- o
        /// <see cref="Feridas"/> e o <see cref="Forma"/>: reliable, so quando MUDA, e reenviado a
        /// quem entra na zona. Custo por tique: zero.
        ///
        /// ============================ E A POSE NAO RESPONDE ISTO ============================
        /// A tentacao seguinte era um valor novo no enum <see cref="Pose"/> -- na epoca sobrava o 7.
        /// Nao servia: pose e o que o corpo ESTA FAZENDO, e o morto do Outro Mundo anda, voa e treina
        /// -- a aureola sumiria a cada passo dele. Ver `Core/World/Alem.cs`.
        ///
        /// (O 7 JA FOI GASTO, e foi gasto pela pergunta certa: <see cref="Pose.Canalizando"/>, que E
        /// o que o corpo esta fazendo. Se a aureola tivesse ficado com ele, o raio e que teria virado
        /// um bit espremido em outro lugar -- e o campo teria acabado do mesmo jeito, so que dizendo
        /// a coisa errada.)
        /// ======================================================================================
        /// </summary>
        Aureola = 45,

        /// <summary>
        /// UMA CENA DO BIO-ANDROIDE COMECOU: um id e QUAL cena (um byte, <c>Core.Forms.CenaBio</c>).
        ///
        /// ============================ POR QUE NAO E O <see cref="Forma"/> ============================
        /// Pelo mesmo argumento que ja tirou o <see cref="Oozaru"/> daquele canal, e ele e mais forte
        /// aqui: DUAS das tres cenas nao sao de forma nenhuma. Elas sobem um `bio_stage` -- estado
        /// PERMANENTE de corpo, que no port viaja como troca de APARENCIA (`Appearance.Corpo`, ver
        /// `Core.Races.BioAndroids`) e nao como entrada do catalogo de formas. Nao ha `de`, nao ha
        /// `para` e nao ha `DegrauDeCena` a mandar: elas acontecem uma vez na vida e nao tem versao
        /// encurtada, porque nao ha maestria em evoluir.
        ///
        /// A terceira (o SSJ2 pela morte) E de forma -- e ela sai pelo <see cref="Forma"/> **tambem**,
        /// com `semCena`, porque a forma tem que chegar a zona inteira do jeito normal. Este pacote
        /// carrega so o que aquele nao sabe dizer: que a cinematica que acompanha aquela forma nao e a
        /// do Super Saiyajin 2, e sim a curta do bio (o DM pula a saiyajin: `DNALabs.dm:697`).
        ///
        /// ============================ PRA ZONA INTEIRA, COMO A <see cref="Furia"/> ============================
        /// No DM os `to_chat` vao pra `view(src)`, o tremor e `Quake()` em todo mundo do planeta e os
        /// feixes de chao sao objetos que ANDAM pelo mapa. Ver alguem virar Cell e informacao que quem
        /// esta em volta tem que ter.
        ///
        /// ============================ E O PACOTE NAO CARREGA PRAZO NEM ARTE ============================
        /// Mesma regra da <see cref="Furia"/>: os relogios (28,0 s a evolucao, 8,0 s o SSJ2) e a folha
        /// de silhueta (`bioto2`/`bioto3`) moram no Core, em `Cinematicas`, e as duas pontas leem o
        /// mesmo arquivo. Mandar qualquer um dos dois criaria uma segunda verdade sobre eles.
        /// ==========================================================================================
        /// </summary>
        CenaDoBio = 46,

        /// <summary>
        /// HA UM CADAVER AO ALCANCE DA MINHA MAO -- o alvo virtual da tecla E.
        ///
        /// ============================ ELE E O IRMAO EXATO DO <see cref="Veiculo"/> ============================
        /// E pelo mesmo motivo, ja escrito la: o menu de interacao procura alvo na LISTA DE CONSTRUCOES
        /// da zona, e um cadaver nao e uma construcao -- ele e um CORPO (ver `Core/World/Cadaver.cs`, e
        /// o porque de ele ser corpo e nao objeto como no DM). Sem este pacote, enterrar seria a unica
        /// acao do jogo sem porta, exatamente como o piloto era a unica pessoa sem acesso ao proprio
        /// veiculo antes daquele.
        ///
        /// ============================ POR QUE O SNAPSHOT NAO RESPONDE ISTO ============================
        /// Ele nem chega perto: o snapshot diz onde os corpos estao, e nao **qual deles e um cadaver**.
        /// Um bit novo pagaria por corpo por tique pra zona inteira -- e "ha um corpo a dois tiles de
        /// mim" muda uma vez por aproximacao e interessa a UMA pessoa. Nao ha bit sobrando de qualquer
        /// forma: os dois bytes de flags do <see cref="EntityState"/> fecharam.
        ///
        /// Entao ele e pessoal e por MUDANCA (nao por tique): sai quando o cadaver mais proximo passa a
        /// ser outro, e sai vazio quando deixa de haver um.
        /// ==========================================================================================
        ///
        /// Formato: `int id` + `string nome`. O id volta ao servidor no verbo `enterrar` e e ele que
        /// desempata dois corpos empilhados; o nome ("corpo de Fulano") e o que o menu ESCREVE, e vem
        /// junto porque o cliente nao tem lista de cadaveres nenhuma -- mesmo argumento que fez o nome
        /// do veiculo viajar junto do tipo dele.
        ///
        /// **ID ZERO QUER DIZER "NENHUM"**: e ele que APAGA a dica quando o jogador se afasta.
        /// </summary>
        Cadaver = 47,

        /// <summary>
        /// A CINEMATICA DA FUSAO COMECOU: **dois ids** -- quem convidou e quem aceitou, nessa ordem.
        ///
        /// Formato: `int dono` + `int passageiro`. Oito bytes, uma vez por fusao.
        ///
        /// ============================ ELE E O IRMAO EXATO DO <see cref="CenaDoBio"/> ============================
        /// Mesmo papel, mesma justificativa: o <see cref="Forma"/> nao serve porque **fundir nao e uma
        /// forma** -- nao ha `de`, nao ha `para`, nao ha <c>DegrauDeCena</c> (nao ha maestria em
        /// fundir) e nao ha versao encurtada. E, como la, o pacote **nao carrega prazo nem arte**: o
        /// relogio da cena, o instante da virada (que e o FIM da animacao da luz --
        /// `Cinematicas.SegundosDaLuzDaFusao`) e a folha (`FusionLight.tres`) moram no Core, em
        /// `Cinematicas.Fusao`, e as duas pontas leem o mesmo arquivo.
        ///
        /// ============================ E POR QUE ELE CARREGA DOIS IDS ============================
        /// Porque esta e **a unica cena do jogo com dois corpos em quadro**. O pedido do dono e literal
        /// sobre isso -- *"UM efeito em cima dos dois personagens"* --, e o segundo id e a unica coisa
        /// que o cliente nao teria como deduzir: no instante em que a cena comeca os dois ainda sao
        /// duas pessoas comuns, sem nada no snapshot que os ligue.
        ///
        /// A ORDEM IMPORTA e ela e a do dono do jogo: quem CONVIDOU e quem controla, e e no corpo dele
        /// que a fusao nasce. O cliente roda a cena naquele corpo e usa o segundo para uma coisa so:
        /// achar o **ponto medio** onde a unica luz da fusao fica (ver `Transformacao._alvoIrmao` e
        /// `Transformacao.PontoMedioDaLuz`).
        ///
        /// ============================ PRA ZONA INTEIRA, COMO A <see cref="CenaDoBio"/> ============================
        /// Dois lutadores virando um, com o chao se soltando e o clarao de tela, e informacao de quem
        /// esta em volta -- e no DM o anuncio da fusao ja sai pra `view(9)` (`Fusion.dm:727`).
        /// ============================================================================================
        /// </summary>
        CenaDeFusao = 48,

        /// <summary>
        /// AS ESFERAS DO DRAGAO DESTA ZONA -- as estatuas, as sete no chao, e o dragao quando esta de pe.
        ///
        /// ============================ TRES COISAS NUM PACOTE, PELO ARGUMENTO DAS OBRAS ============================
        /// A <see cref="Construcoes"/> ja leva construcao, nave parada e mobilia de interior no mesmo
        /// pacote, e o cabecalho do `MandarObras` explica: *"nao por economia de opcode, porque pro
        /// cliente uma nave POUSADA e exatamente o que uma construcao e"*. Vale igual aqui -- estatua,
        /// esfera e dragao sao a mesma coisa pro cliente: um sprite ancorado num ponto da zona, no
        /// Y-sort, com um nome. O <see cref="CoisaDeEsfera"/> na frente diz qual e.
        ///
        /// ============================ E A FOLHA VIAJA COMO SIMBOLO, NAO COMO `res://` ============================
        /// Aqui esta a diferenca em relacao a <see cref="Construcoes"/>, e ela e de PROCEDENCIA: la o
        /// caminho vem do `construcoes.json`, que e dado extraido; aqui nao ha catalogo, e cravar
        /// `res://` no servidor seria o servidor conhecendo a arvore de assets do Godot -- exatamente o
        /// que a regra 0.1 da casa proibe. Entao viaja "comum"/"namek"/"estatua"/"shenron"/"porunga", e
        /// quem traduz e o `EsferaDesenhada.FolhaDe` do cliente.
        /// =====================================================================================================
        ///
        /// So quando MUDA -- nunca por tique. Nascer, espalhar, pegar, largar e invocar sao eventos.
        /// </summary>
        Esferas = 49,

        /// <summary>
        /// AS SUPER ESFERAS QUE EU ALCANCO, o meu placar, e o sinal do radar dourado.
        ///
        /// ============================ ELE NAO MANDA O CICLO, E ISSO E O SIGILO ============================
        /// A posicao das sete e funcao pura de (semente do universo, numero, ciclo) e o cliente ja tem a
        /// semente -- ou seja, o unico dado que falta pra montar o mapa do tesouro inteiro e o CICLO, e
        /// ele nao viaja. O que viaja e o que se ve daqui (recorte por CHUNK, o mesmo do resto do
        /// espaco) e uma FRASE de radar que o servidor ja resolveu.
        ///
        /// E o `SaveItem = 0` do original dito de outro jeito: *"o obj so existe enquanto o setor esta
        /// carregado"* (`ProceduralSpace.dm:1503`).
        /// ==========================================================================================
        /// </summary>
        SuperEsferas = 50,

        /// <summary>
        /// **O REFUGIO**: o planeta natal desta pessoa foi destruido, e estas sao as saidas.
        ///
        /// ============================ POR QUE UM PACOTE, E NAO CHAT ============================
        /// A conquista inteira responde em texto de proposito (ver `MenuJogo.BlocoDeDominio`), e
        /// isso continua certo la: sao verbos que o servidor responde com uma frase. **Aqui nao**:
        /// o jogador precisa ESCOLHER entre duas coisas que so o servidor conhece -- quais dominios
        /// dele ainda estao de pe (livro de dominios) e quais mundos vivos sobraram perto de casa
        /// (a vizinhanca depende de quais planetas morreram, que e estado de servidor).
        ///
        /// O cliente calcula a vizinhanca sozinho na CRIACAO (`Bercos.IrmasDoNatal`, funcao pura) e
        /// nao consegue calcular esta: la nenhum planeta tinha morrido ainda.
        /// ===================================================================================
        ///
        /// `precisa = false` quer dizer "o seu berco esta de pe" -- e como a tela some quando o
        /// planeta volta (um desejo das Esferas ressuscita mundo). Sem esse caso, a escolha de uma
        /// catastrofe que ja passou ficaria na tela pra sempre.
        ///
        /// `abrir` e o servidor PEDINDO pra tela aparecer agora (a chegada ao Outro Mundo, uma vez
        /// por sessao). Sem ele, o mesmo pacote e so uma atualizacao de quem ja esta olhando.
        /// </summary>
        Refugio = 51,
    }

    /// <summary>
    /// O QUE E ESTA COISA no pacote <see cref="S2C.Esferas"/>.
    ///
    /// Byte explicito em vez de numero magico (a alternativa era "numero 0 = estatua, 8 = dragao"):
    /// um discriminador que se le e um discriminador que ninguem quebra por engano seis meses depois.
    /// </summary>
    public enum CoisaDeEsfera : byte
    {
        /// <summary>A `obj/DragonStatue` -- o set inteiro pendurado nela.</summary>
        Estatua = 0,

        /// <summary>Uma das sete `obj/DB`, com o numero de estrelas em `numero`.</summary>
        Esfera = 1,

        /// <summary>O `obj/DragonObject` -- 256x353 px, de pe por pouco tempo.</summary>
        Dragao = 2,
    }

    /// <summary>
    /// A ESCALA DO SPRITE DE UM TIRO, em 1/20 -- o `A.transform *= wavemult` do DM (`beams.dm:149`).
    ///
    /// UM BYTE, e nao um float: o `wavemult` do jogo inteiro vive entre 1 e 4 (o maior e o Final
    /// Flash), o passo de 0,05 e menor que um pixel num sprite de 32, e o teto de 12,75 e o triplo
    /// do maior que existe. Quatro bytes de float seriam tres bytes pra descrever precisao que
    /// nenhuma tela mostra.
    ///
    /// O CLAMP E DOS DOIS LADOS de proposito. Piso em 1/20 (e nao zero) porque escala zero e um tiro
    /// INVISIVEL -- uma tecnica futura com `MultDeOnda` mal preenchido apagaria o proprio efeito, e
    /// esse e o tipo de defeito que so aparece jogando. Ver <see cref="DeEscalaDeProjetil"/>.
    /// </summary>
    public static byte EscalaDeProjetilEmByte(double escala) =>
        (byte)Math.Clamp(Math.Round(escala * 20), 1, 255);

    /// <summary>O caminho de volta do <see cref="EscalaDeProjetilEmByte"/>.</summary>
    public static float DeEscalaDeProjetil(byte b) => b / 20f;

    /// <summary>Os dois instantes de um ataque de ki. Ver <see cref="S2C.Projetil"/>.</summary>
    public enum ProjetilSub : byte
    {
        /// <summary>
        /// Saiu da mao de alguem: id do tiro, id do dono, tipo, a ARTE, a ESCALA e onde. O cliente ja
        /// pode desenha-lo antes do primeiro snapshot chegar.
        ///
        /// ============================ POR QUE A ARTE VIAJA AQUI E NAO NO SNAPSHOT ============================
        /// A pergunta *"que folha este tiro desenha"* nao muda depois do disparo -- e ela nao e
        /// derivavel do que ja viaja: o <see cref="ProjetilState.Tipo"/> tem DOIS BITS e responde
        /// como o tiro se COMPORTA (raio, bola, teleguiada), nao com que arte; e o id da tecnica nao
        /// esta em pacote nenhum. Com vinte e quatro tecnicas atirando por tres tipos, derivar
        /// significaria desenhar Kamehameha, Masenko e Galick Ho identicos.
        ///
        /// Entao ela entra AQUI: **3 bytes por disparo** (`ushort` da arte + `byte` da escala), no
        /// canal confiavel, uma vez. Alargar o `Tipo` do <see cref="ProjetilState"/> teria custado o
        /// mesmo dado por tiro POR TIQUE POR ZONA, a 30 Hz -- e o orcamento do snapshot e o que esse
        /// struct inteiro existe pra defender.
        /// ==============================================================================================
        /// </summary>
        Nasceu = 0,

        /// <summary>
        /// Acabou: id do tiro, o motivo (<c>Core.Combat.FimDeProjetil</c>) e onde. O motivo escolhe o
        /// efeito -- estouro em corpo, lasca em parede, apagar no ar.
        /// </summary>
        Morreu = 1,
    }

    /// <summary>
    /// QUE DISPUTA E ESTA -- o primeiro byte do <see cref="ClashSub.Comecou"/>.
    ///
    /// ============================ UM OPCODE, TRES DISPUTAS ============================
    /// O ZanzoClash e o embate de ki sao a MESMA conversa no fio: comecou, letra, placar, veredito,
    /// acabou. Dar opcode proprio ao segundo duplicaria as cinco mensagens, os cinco eventos do
    /// cliente e a tela inteira do quick time event -- e a instrucao desta camada foi explicita:
    /// *"nao escreva um segundo embate; generalize se precisar"*.
    ///
    /// O que muda entre eles e SO O VOCABULARIO na tela (o titulo, a dica e a frase do desfecho), e
    /// pra isso um byte basta. O `Placar` serve os dois porque um cabo de guerra e um cabo de guerra:
    /// no ZanzoClash sao pontos meus contra pontos dele, no embate de ki e o medidor de 0 a 100
    /// contra o que falta dele -- a barra desenha `meus / (meus + dele)` nos dois casos.
    /// ==================================================================================
    /// </summary>
    public enum TipoDeEmbate : byte
    {
        /// <summary>ZANZO CLASH: dois corpos rapidos demais pra vista. Ver `GameServer.ZanzoClash.cs`.</summary>
        Velocidade = 0,

        /// <summary>Dois FEIXES se encontrando no ar. `BeamClash.dm`.</summary>
        FeixeContraFeixe = 1,

        /// <summary>Um feixe contra as MAOS de quem aguenta. Ver `EmbateDeKi.PoderDeSegurar`.</summary>
        FeixeContraGuarda = 2,

        /// <summary>
        /// A DANCA DA FUSAO. Ver `GameServer.Fusao.cs`.
        ///
        /// ============================ ELA E A UNICA QUE NAO E CABO DE GUERRA ============================
        /// Nos outros tres, o placar de um e o buraco do outro e alguem sai perdendo. Aqui **os dois
        /// ganham juntos ou os dois saem estragados**: o placar sao os passos acertados de cada um, e
        /// o desfecho e o mesmo pros dois lados. O byte entra nesta enum mesmo assim porque a CONVERSA
        /// e identica (comecou, letra, placar, veredito, acabou) -- o que ele muda e so o vocabulario
        /// da tela, que e exatamente pra isso que este byte existe.
        /// ==========================================================================================
        /// </summary>
        Fusao = 3,
    }

    /// <summary>Os momentos de um embate -- ZanzoClash e colisao de ki. Ver <see cref="S2C.Clash"/>.</summary>
    public enum ClashSub : byte
    {
        /// <summary>
        /// Comecou. PESSOAL, um pacote por lutador: o <see cref="TipoDeEmbate"/>, eu, o outro,
        /// quantos ms dura, quanto vale cada acerto MEU e quanto vale cada acerto DELE (a vantagem
        /// de poder).
        /// </summary>
        Comecou = 0,
        /// <summary>Uma tecla nova: a letra (byte ASCII) e o prazo em ms.</summary>
        Tecla = 1,
        /// <summary>O placar: os meus pontos e os dele, pra barra de cabo de guerra.</summary>
        Placar = 2,
        /// <summary>
        /// Um baque em (x, y). No ZanzoClash e o cruzamento invisivel dos dois corpos; no embate de
        /// ki e o ponto de encontro estourando -- que ANDA, e por isso o ponto viaja no pacote.
        /// </summary>
        Baque = 3,

        /// <summary>
        /// Acabou: quem venceu e quem perdeu. **Os dois zerados = EMPATE** (`draw()` do
        /// `BeamClash.dm:355`), que so o embate de ki produz: no ZanzoClash o desempate por poder
        /// garante que sempre ha um vencedor.
        /// </summary>
        Acabou = 4,

        /// <summary>
        /// UM VISLUMBRE: os dois corpos aparecem por um instante, trocando um golpe, e somem de
        /// novo. Um byte -- 1 aparece, 0 some.
        ///
        /// So os DOIS recebem: quem assiste ja ve pelo `Oculto` do snapshot, que e a mesma
        /// verdade e chega pelo mesmo caminho de sempre. Este pacote existe porque o corpo LOCAL
        /// nao vem por snapshot -- quem esta no embate esconde o proprio boneco por conta.
        /// </summary>
        Vislumbre = 5,

        /// <summary>
        /// O VEREDITO DA LETRA QUE EU ACABEI DE APERTAR -- um byte, 1 acertei, 0 errei.
        ///
        /// ============================ POR QUE NAO DA PRA DEDUZIR DO PLACAR ============================
        /// O cliente sabe que letra foi pedida e que tecla ele mandou, mas quem JULGA e o servidor
        /// (de proposito -- ver `TeclaDoEmbate`), e ele julga tambem o PRAZO: uma letra certa que
        /// chega tarde e um erro, e daqui nao da pra saber disso.
        ///
        /// Deduzir do placar tambem nao serve: ele chega pros dois lados a cada mudanca, entao o
        /// meu placar tambem mexe quando o OUTRO acerta -- eu piscaria verde pelo acerto dele.
        ///
        /// E PESSOAL: so quem apertou recebe. O adversario nao tem o que fazer com isso.
        /// ==============================================================================================
        /// </summary>
        Julgou = 6,
    }

    /// <summary>
    /// UMA PORTA MUDOU DE ESTADO -- ou, com <paramref name="completo"/>, ESTA E A LISTA INTEIRA.
    ///
    /// O mesmo pacote serve pros dois casos de proposito. Entrar numa zona precisa do estado de
    /// TODAS as portas dela (senao uma que ficou aberta apareceria fechada pra quem chega), e uma
    /// porta que abre precisa de uma mensagem so. Dois pacotes pra isso seriam duas leituras a
    /// manter em sincronia -- e a lista completa e minuscula: 99 portas no jogo INTEIRO, no maximo
    /// 29 numa zona (Namek), e so as ABERTAS viajam.
    ///
    /// `completo = 1` quer dizer "feche tudo que nao estiver aqui". E o que devolve o mapa ao
    /// estado de arquivo antes de aplicar o que o servidor sabe -- ver `ZoneCollision.FecharTudo`.
    /// </summary>
    /// <summary>
    /// A mascara de feridas: cinco bytes crus. Ver <see cref="Protocol.S2C.Feridas"/>.
    ///
    /// Escrita e leitura no MESMO lugar de proposito: o formato tem cinco campos anonimos e um
    /// leitor escrito noutro arquivo e um leitor que vai destoar quando o corpo ganhar uma zona.
    /// </summary>
    public static void PutFeridas(this NetDataWriter w, Jandirus.Core.Combat.MascaraDeFeridas m)
    {
        for (int i = 0; i < Jandirus.Core.Combat.MascaraDeFeridas.Zonas; i++) w.Put(m.Bruto(i));
        w.Put(m.AmputadosBruto);   // os quatro membros que somem quando arrancados
    }

    public static Jandirus.Core.Combat.MascaraDeFeridas GetFeridas(this NetDataReader r) =>
        new(r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte());

    public static void PutPortas(this NetDataWriter w, bool completo, IReadOnlyList<(int X, int Y, bool Aberta)> portas)
    {
        w.Put(completo);
        w.Put((ushort)Math.Min(portas.Count, ushort.MaxValue));
        for (int i = 0; i < portas.Count && i < ushort.MaxValue; i++)
        {
            w.Put((ushort)portas[i].X);
            w.Put((ushort)portas[i].Y);
            w.Put(portas[i].Aberta);
        }
    }

    public static (bool Completo, List<(int X, int Y, bool Aberta)> Portas) GetPortas(this NetDataReader r)
    {
        bool completo = r.GetBool();
        int n = r.GetUShort();
        var l = new List<(int, int, bool)>(n);
        for (int i = 0; i < n; i++) l.Add((r.GetUShort(), r.GetUShort(), r.GetBool()));
        return (completo, l);
    }

    /// <summary>
    /// O QUE O PERSONAGEM SABE FAZER, em bits. E o que decide quais ABAS existem no menu.
    ///
    /// No BYOND essa decisao era espalhada (`if(savant.gotsense) register_html_tab("Sense")`,
    /// `if(hasnav) tabs += "Nav"`, `if(scouteron) Sense vira Scan`). Aqui e um campo so, que o
    /// servidor preenche -- o cliente nunca decide sozinho que tem uma habilidade.
    /// </summary>
    [Flags]
    public enum Poder : uint
    {
        Nenhum = 0,
        Sense = 1 << 0,      // aprendeu a skill: a aba "Sense" passa a existir
        Scouter = 1 << 1,    // scouter LIGADO: a aba "Sense" vira "Scan" (leitura exata de BP)
        Nav = 1 << 2,        // nav system: a aba "Nav" e o minimapa do espaco
        Admin = 1 << 3,
    }

    /// <summary>
    /// A FICHA QUE QUASE NAO MUDA: os oito atributos e o resto do que o painel de status
    /// mostrava. Vai num pacote separado do <see cref="SheetState"/> de proposito -- aquele sai
    /// varias vezes por segundo porque carrega vida e Ki, e atributo so muda quando se treina.
    ///
    /// Sao os `R...` do original (`Rphysoff`, `Rphysdef`, ...): o valor DEPOIS dos modificadores
    /// raciais e de classe, que e o que o `ui_tab_stats` lia.
    /// </summary>
    public struct AtributosState
    {
        public float PhysOff, PhysDef, KiOff, KiDef, Technique, KiSkill, Speed, Esoteric;
        public float Willpower, Stamina;
        public uint Poderes;
        public int Idade;
        public string Raca;

        /// <summary>Em que forma estou (o id de <c>Core.Forms.Forma</c>). 0 = base.</summary>
        public ushort FormaAtual;

        /// <summary>
        /// Quanto domino de CADA forma que ja toquei. Vai aqui, e nao num pacote proprio,
        /// porque maestria muda devagar (100% leva ~3h dentro da forma) -- e exatamente o
        /// perfil da ficha lenta.
        /// </summary>
        public (ushort Forma, float Pct)[] Maestrias;

        /// <summary>
        /// A DISCIPLINA DIVINA: 0 = nenhuma, 1 = Ultra Instinto, 2 = Poder da Destruicao.
        ///
        /// Vem junto da ficha lenta pelo mesmo motivo das maestrias: as duas energias mudam em
        /// fracoes por segundo, nao por quadro. E o cliente precisa DAS DUAS -- a REAL pra saber
        /// quais botoes existem, a ATUAL pra mostrar quanto resta antes de a passiva virar enfeite.
        /// </summary>
        public byte Disciplina;
        public float DiscReal, DiscAtual;
        public bool DiscLigada;

        /// <summary>
        /// ============================ O MULTIPLICADOR DE TREINO, E AS PARTES DELE ============================
        /// <see cref="GanhoDeTreino"/> e o `bp_gain_mult()` do DM (`HtmlUI.dm:108`) ja calculado pelo
        /// SERVIDOR: quantas vezes o treino de agora rende, comparado com uma sessao neutra
        /// (gravidade 1, sem peso, fora de zona especial).
        ///
        /// **Ele vem pronto, e nao em pedacos pro cliente montar.** Peso, gravidade, aclimatacao,
        /// folego e Sala do Tempo entram numa formula que ja existe uma vez no `Core`
        /// (`Fighter.MultiplicadorDeGanho`); refaze-la aqui seria a segunda copia que a PARTE 3 do
        /// plano manda evitar -- e a copia do CLIENTE e sempre a que envelhece calada, porque so o
        /// servidor tem `Egains`, `GravMastered` e `zoneGainMult`.
        ///
        /// Os quatro campos ao lado sao os PEDACOS, e eles existem so pra a frase entre parenteses:
        /// "2800x (10x grav · 1,4x pesos · Sala 280x)". Sem eles o jogador leria um numero magico e
        /// nao saberia o que mudar pra ele subir -- que e o mesmo que nao mostrar nada.
        ///
        /// <see cref="Esmagamento"/> e a razao de esmagamento (gravidade/maestria, ou o peso, o que
        /// for pior). Ela e o AVISO da conta: acima de 1 o corpo perde vida e velocidade, e a partir
        /// de `Core.Stats.Esmagamento.RazaoQuePrende` ele fica preso no chao -- e o cliente le este
        /// mesmo campo pra parar de tentar andar, em vez de brigar com as correcoes do servidor.
        /// ==================================================================================================
        /// </summary>
        public float GanhoDeTreino, Gravidade, GravEfetiva, PesoMult, ZonaMult, Esmagamento;

        /// <summary>
        /// ============================ A SESSAO DA SALA DO TEMPO ============================
        /// <see cref="SalaFase"/>: 0 = nao esta na Sala, 1 = sessao rendendo, 2 = acabou e a janela
        /// de saida esta aberta, 3 = PRESO. <see cref="SalaMinutos"/> sao os minutos REAIS que
        /// faltam pra a fase virar (a sessao acabar, ou a porta trancar).
        ///
        /// **ELES VEM PRONTOS, e pelo mesmo motivo do multiplicador acima**: o cliente nao tem o
        /// relogio do mundo do servidor, nao sabe quantos dias in-game a sessao gastou e nao pode
        /// saber quando a janela foi armada (ela e um estado vivo do servidor). Uma conta de tempo
        /// duplicada seria a que atrasa e mostra "restam 2 minutos" pra alguem que ja esta preso.
        ///
        /// A CONTA E EM DIAS IN-GAME do lado de la (ver `Core/World/SalaDoTempo.cs`); o que viaja e
        /// minuto real porque e o que se le num relogio -- ninguem conta a propria vida em dias de
        /// um planeta de 24 minutos.
        /// ================================================================================
        /// </summary>
        public byte SalaFase;
        public float SalaMinutos;

        public readonly bool Tem(Poder p) => (Poderes & (uint)p) != 0;

        public readonly void Write(NetDataWriter w)
        {
            w.Put(PhysOff); w.Put(PhysDef); w.Put(KiOff); w.Put(KiDef);
            w.Put(Technique); w.Put(KiSkill); w.Put(Speed); w.Put(Esoteric);
            w.Put(Willpower); w.Put(Stamina);
            w.Put(Poderes);
            w.Put(Idade);
            w.Put(Raca ?? "");
            w.Put(FormaAtual);
            (ushort, float)[] ms = Maestrias ?? [];
            w.Put((byte)Math.Min(ms.Length, 255));
            for (int i = 0; i < ms.Length && i < 255; i++) { w.Put(ms[i].Item1); w.Put(ms[i].Item2); }
            w.Put(Disciplina); w.Put(DiscReal); w.Put(DiscAtual); w.Put(DiscLigada);
            w.Put(GanhoDeTreino); w.Put(Gravidade); w.Put(GravEfetiva);
            w.Put(PesoMult); w.Put(ZonaMult); w.Put(Esmagamento);
            w.Put(SalaFase); w.Put(SalaMinutos);
        }

        public static AtributosState Read(NetDataReader r) => new()
        {
            PhysOff = r.GetFloat(), PhysDef = r.GetFloat(), KiOff = r.GetFloat(), KiDef = r.GetFloat(),
            Technique = r.GetFloat(), KiSkill = r.GetFloat(), Speed = r.GetFloat(), Esoteric = r.GetFloat(),
            Willpower = r.GetFloat(), Stamina = r.GetFloat(),
            Poderes = r.GetUInt(),
            Idade = r.GetInt(),
            Raca = r.GetString(24),
            FormaAtual = r.GetUShort(),
            Maestrias = LerMaestrias(r),
            Disciplina = r.GetByte(),
            DiscReal = r.GetFloat(),
            DiscAtual = r.GetFloat(),
            DiscLigada = r.GetBool(),
            GanhoDeTreino = r.GetFloat(),
            Gravidade = r.GetFloat(),
            GravEfetiva = r.GetFloat(),
            PesoMult = r.GetFloat(),
            ZonaMult = r.GetFloat(),
            Esmagamento = r.GetFloat(),
            SalaFase = r.GetByte(),
            SalaMinutos = r.GetFloat(),
        };

        private static (ushort, float)[] LerMaestrias(NetDataReader r)
        {
            int n = r.GetByte();
            var v = new (ushort, float)[n];
            for (int i = 0; i < n; i++) v[i] = (r.GetUShort(), r.GetFloat());
            return v;
        }
    }

    /// <summary>Quantos caracteres uma fala pode ter. O BYOND cortava em 1000; aqui tambem.</summary>
    public const int MaxFala = 1000;

    /// <summary>Um membro, do jeito que o boneco de dano precisa ver.</summary>
    public struct ParteState
    {
        public string Nome;
        public byte Vida;       // 0-100
        public bool Decepado;
    }

    /// <summary>
    /// O CORPO EM PARTES, do dono pro dono. Nao vai no snapshot e nao vai pra mais ninguem:
    /// saber que o inimigo esta com o braco direito a 12% e informacao de ficha.
    ///
    /// So sai quando algo MUDA -- o servidor compara uma assinatura, exatamente como o
    /// original fazia (`last_sig` do LimbHPIndicator), porque redesenhar 15 overlays tres
    /// vezes por segundo sem nada ter mudado e trabalho jogado fora nas duas pontas.
    ///
    /// Manda o NOME de cada parte em vez de confiar na ordem: um Saiyajin tem 15 partes e um
    /// humano 14, e um dia alguma raca tera outra coisa. Indice posicional aqui seria um bug
    /// esperando o primeiro membro novo.
    /// </summary>
    public static void PutCorpo(this NetDataWriter w, IReadOnlyList<ParteState> partes)
    {
        w.Put((byte)Math.Min(partes.Count, 255));
        for (int i = 0; i < partes.Count && i < 255; i++)
        {
            w.Put(partes[i].Nome);
            w.Put((byte)(partes[i].Decepado ? 255 : partes[i].Vida));
        }
    }

    public static List<ParteState> GetCorpo(this NetDataReader r)
    {
        int n = r.GetByte();
        var l = new List<ParteState>(n);
        for (int i = 0; i < n; i++)
        {
            string nome = r.GetString(32);
            byte v = r.GetByte();
            // 255 e a marca de DECEPADO, nao "vida 255": um membro que nao existe mais nao
            // tem vida, e o boneco pinta ele de roxo em vez de verde
            l.Add(new ParteState { Nome = nome, Vida = v == 255 ? (byte)0 : v, Decepado = v == 255 });
        }
        return l;
    }

    /// <summary>
    /// O relato de um golpe, do jeito que a rede carrega.
    ///
    /// O DANO SO VAI PROS DOIS ENVOLVIDOS. Quem so esta assistindo recebe o evento sem numero
    /// -- ve o impacto, ouve o som, mas nao le a ficha alheia. Isso e a mesma regra do BP
    /// invisivel: informacao de poder nao viaja de graca so porque alguem estava por perto.
    /// </summary>
    public struct HitEvent
    {
        public int Atacante, Alvo;
        public byte Desfecho;

        /// <summary>
        /// Peso do baque: 1 pequeno, 2 medio, 3 grande. Quem escolhe e o SERVIDOR (e ele
        /// quem conhece o combo), pra os dois lados ouvirem o MESMO impacto.
        /// </summary>
        public byte Nivel;
        public bool TemDano;
        public float Dano;
        public string Membro;

        /// <summary>
        /// DE ONDE O SOCO SAIU -- a posicao do atacante NO INSTANTE em que o servidor resolveu.
        ///
        /// ============================ POR QUE ELA PRECISA VIAJAR ============================
        /// O arranque do soco (`Aproximar`) e um TELEPORTE de servidor de ate 128 px, e ele
        /// acontece no mesmo tratador de pacote que anuncia o golpe. A posicao nova so alcanca
        /// quem assiste no snapshot do PROXIMO tique (ate 33 ms depois) -- e mesmo la o corpo
        /// remoto nao salta, ele INTERPOLA por mais um intervalo.
        ///
        /// Sem esta coordenada o cliente desenhava a faisca no meio das posicoes DESENHADAS: o
        /// atacante ainda no ponto de partida, a vitima no lugar dela, e o baque estourando no
        /// chao vazio entre os dois. O dono fotografou isso e leu como o que parecia ser:
        /// "percebi q as vezes a hitbox pega MUITO longe". A hitbox nunca passou de 40 px -- o
        /// DESENHO e que estava atrasado.
        ///
        /// E o mesmo remedio que este projeto ja aplicou pro vulto do Zanzoken, que nascia no
        /// lugar errado pela mesma razao (ver `S2C.Zanzo` e `World.AoPiscar`). A faisca tinha
        /// ficado de fora daquele conserto.
        ///
        /// SO VIAJA QUANDO HA ALVO: soco no ar nao desenha faisca, entao nao paga os 8 bytes.
        /// </summary>
        public Vec2 PosAtacante;
        public bool Quebrou, Decepou, Nocauteou, Morreu, Rabo;

        // O BIT 32 DESTE BYTE ESTA LIVRE. Ele carregava um `Zanzo` ("houve vulto"), e o vulto
        // passou a viajar pelo `S2C.Zanzo`, que leva tambem a POSICAO de onde o corpo saiu --
        // sem ela quem assistia desenhava a miragem no lugar errado.

    /// <summary>
    /// HOUVE INVESTIDA -- o corpo REALMENTE fechou a distancia.
    ///
    /// Separado do <see cref="Zanzo"/> porque sao coisas diferentes: investir e do golpe pesado
    /// com alguem no alcance; a miragem exige a SKILL por cima disso. O dono relatou o sintoma de
    /// os dois nao existirem: "ao ficar parado e bater segurando o shift ele faz o som de corrida"
    /// -- o cliente tocava o rasgo na tecla, sem saber se houve arranque.
    ///
    /// Cabe no bit 64 do mesmo byte de desfechos. Ainda sobra um.
    /// </summary>
    public bool Investiu;

    /// <summary>
    /// QUEM ESQUIVOU tem a Afterimage -- e por isso deixa vulto no lugar.
    ///
    /// PRECISA SER UM BIT PROPRIO: o <see cref="Zanzo"/> fala da skill do ATACANTE (a miragem da
    /// investida). Reusar aquele desenharia o vulto da esquiva com base na skill da pessoa errada
    /// -- quem apanhou apareceria piscando porque quem BATEU sabe Zanzoken.
    ///
    /// E o ultimo bit livre do byte de desfechos (128).
    /// </summary>
    public bool ZanzoEsquiva;

        public void Write(NetDataWriter w)
        {
            w.Put(Atacante); w.Put(Alvo); w.Put(Desfecho); w.Put(Nivel);
            if (Alvo != 0) w.PutVec(PosAtacante);   // ver `PosAtacante`
            w.Put(TemDano);
            if (TemDano) { w.Put(Dano); w.Put(Membro ?? ""); }
            w.Put((byte)((Quebrou ? 1 : 0) | (Decepou ? 2 : 0) | (Nocauteou ? 4 : 0)
                       | (Morreu ? 8 : 0) | (Rabo ? 16 : 0) | (Investiu ? 64 : 0) | (ZanzoEsquiva ? 128 : 0)));
        }

        public static HitEvent Read(NetDataReader r)
        {
            var h = new HitEvent
            {
                Atacante = r.GetInt(), Alvo = r.GetInt(), Desfecho = r.GetByte(),
                Nivel = r.GetByte(), Membro = "",
            };
            if (h.Alvo != 0) h.PosAtacante = r.GetVec();
            h.TemDano = r.GetBool();
            if (h.TemDano) { h.Dano = r.GetFloat(); h.Membro = r.GetString(32); }
            byte f = r.GetByte();
            h.Quebrou = (f & 1) != 0; h.Decepou = (f & 2) != 0;
            h.Nocauteou = (f & 4) != 0; h.Morreu = (f & 8) != 0; h.Rabo = (f & 16) != 0;
            h.Investiu = (f & 64) != 0;
            h.ZanzoEsquiva = (f & 128) != 0;
            return h;
        }
    }

    // ---------------------------------------------------------------------
    // helpers de leitura/escrita dos tipos do Core
    // ---------------------------------------------------------------------
    public static void PutVec(this NetDataWriter w, Vec2 v) { w.Put(v.X); w.Put(v.Y); }
    public static Vec2 GetVec(this NetDataReader r) => new(r.GetFloat(), r.GetFloat());

    public static void PutZone(this NetDataWriter w, ZoneKey z)
    {
        w.Put(z.Kind);
        w.Put(z.Name);
        w.Put(z.Seed);
    }

    public static ZoneKey GetZone(this NetDataReader r)
    {
        byte kind = r.GetByte();
        string name = r.GetString();
        ulong seed = r.GetULong();
        return new ZoneKey(kind, name, seed);
    }

    /// <summary>A ficha da criacao. O servidor NAO confia nela pra stat: so pros dados de
    /// identidade (nome/raca/genero/idade) e pra linhagem NAS 3 racas em que ela e escolhida.</summary>
    public static void PutDraft(this NetDataWriter w, CharacterDraft d)
    {
        w.Put(d.Name); w.Put(d.Race); w.Put(d.Planet);
        w.Put(d.Gender); w.Put(d.Age); w.Put(d.ChosenClass);
        // O PORTE MEXE EM STAT, entao o servidor precisa dele -- e vai validar contra a lista.
        w.Put(d.Porte);
        w.Put(d.Backstory);
        // O PEDIDO DE BERCO. Um bit, e nao um planeta: ver `CharacterDraft.PertoDeCasa`.
        w.Put(d.PertoDeCasa);
    }

    public static CharacterDraft GetDraft(this NetDataReader r) => new()
    {
        Name = r.GetString(24), Race = r.GetString(24), Planet = r.GetString(24),
        Gender = r.GetString(8), Age = r.GetInt(), ChosenClass = r.GetString(32),
        Porte = r.GetString(16),
        // O TETO DA LEITURA E MAIOR QUE O DA REGRA de proposito. `GetString(max)` do LiteNetLib
        // nao trunca: acima do teto ele consome os bytes e devolve VAZIO. Cortar em 500 aqui faria
        // uma historia de 501 caracteres chegar como historia vazia, e o jogador leria "escreva a
        // historia" depois de ter escrito demais. Lendo com folga, quem recusa e o `Validar`, que
        // sabe dizer o motivo certo.
        Backstory = r.GetString(CharacterDraft.BackstoryMax * 2),
        PertoDeCasa = r.GetBool(),
    };

    /// <summary>Cor OPCIONAL: um byte de presenca antes. Ausente = a cor natural do sprite.</summary>
    public static void PutRgb(this NetDataWriter w, Rgb? c)
    {
        w.Put(c.HasValue);
        w.Put(c?.R ?? 0); w.Put(c?.G ?? 0); w.Put(c?.B ?? 0);
    }

    public static Rgb? GetRgb(this NetDataReader r)
    {
        bool tem = r.GetBool();
        var c = new Rgb(r.GetByte(), r.GetByte(), r.GetByte());
        return tem ? c : null;
    }

    /// <summary>
    /// A aparencia escolhida na criacao. Vai junto do JoinRequest porque no desenho do dono
    /// TODA a criacao acontece antes de conectar -- quando a conexao abre, o personagem ja
    /// esta pronto. O servidor SANEIA o que chegar contra o catalogo dele.
    /// </summary>
    public static void PutAppearance(this NetDataWriter w, Appearance a)
    {
        w.Put((byte)a.Corpo);
        w.Put((byte)a.Tom);
        w.PutRgb(a.CorPele);
        w.Put(a.Cabelo);
        w.PutRgb(a.CorCabelo);
        w.PutRgb(a.CorOlho);
        w.Put((byte)Math.Min(a.Roupa.Count, Appearance.MaxRoupa));
        for (int i = 0; i < a.Roupa.Count && i < Appearance.MaxRoupa; i++)
        {
            // O CAMINHO E A COR, nesta ordem -- e a leitura tem que casar byte a byte.
            w.Put(a.Roupa[i].Caminho);
            w.PutRgb(a.Roupa[i].Cor);
        }

        // OS CORPOS DAS FORMAS DO FROST DEMON, no fim do pacote. Vazio pra todas as outras racas,
        // e ai custa um byte -- o preco de nao ter um segundo pacote so pra uma raca.
        w.Put((byte)Math.Min(a.FormasDeFrost.Count, Jandirus.Core.Races.FormasDeFrost.Total));
        for (int i = 0; i < a.FormasDeFrost.Count && i < Jandirus.Core.Races.FormasDeFrost.Total; i++)
            w.Put(a.FormasDeFrost[i]);

        // A COR DA CHAMA DESTE CORPO, no fim de tudo. Quatro bytes (ver `PutRgb`), uma vez por
        // pessoa por zona -- o `PeerLook` nao repete.
        //
        // SEM BUMP DE VERSAO e sem tolerancia, de proposito: `Protocol.ConnectionKey` e a unica
        // porta e o `AcceptIfKey` nao olha build. Cliente e servidor sobem juntos (auto-host /
        // `servidor.bat`), e o precedente e o proprio `FormasDeFrost` logo acima, anexado do mesmo
        // jeito. O que isso EXIGE e que escrita e leitura andem no mesmo commit -- um dos dois
        // sozinho desalinha o pacote inteiro, e o `SlotList` manda tres aparencias em fila.
        w.PutRgb(a.CorAura);

        // E A COR DO TIRO logo depois -- o SEGUNDO sorteio do original (`CharacterCreation.dm:28`),
        // que este port tinha colapsado no primeiro. Mesmos quatro bytes, mesma disciplina de
        // "escrita e leitura andam no mesmo commit" do bloco acima. Ver `Appearance.CorKi`, que
        // explica por que a chama e o tiro nao podem compartilhar a cor.
        w.PutRgb(a.CorKi);
    }

    public static Appearance GetAppearance(this NetDataReader r)
    {
        var a = new Appearance
        {
            Corpo = r.GetByte(),
            Tom = r.GetByte(),
            CorPele = r.GetRgb(),
            Cabelo = r.GetString(40),
            CorCabelo = r.GetRgb(),
            CorOlho = r.GetRgb(),
        };
        // o teto vem do PROTOCOLO, nao do que o pacote afirma: um byte diz ate 255 pecas e
        // ler 255 strings de um cliente forjado seria trabalho de graca pro servidor
        int n = Math.Min((int)r.GetByte(), Appearance.MaxRoupa);
        for (int i = 0; i < n; i++)
        {
            // A COR E LIDA SEMPRE, mesmo quando o caminho e descartado.
            //
            // O `GetString(max)` do LiteNetLib NAO trunca: acima do teto ele CONSOME os bytes e
            // devolve string VAZIA. Pular a leitura da cor junto desalinharia o resto do pacote --
            // e o `SlotList` manda tres aparencias em fila, entao um desalinho vira "meu
            // personagem sumiu". Le-se o par inteiro; descarta-se depois.
            string caminho = r.GetString(120);
            Rgb? cor = r.GetRgb();
            if (caminho.Length > 0) a.Roupa.Add(new PecaDeRoupa(caminho, cor));
        }

        // MESMO TETO DO PROTOCOLO, pela mesma razao da roupa: o byte diz ate 255, e ler 255 nomes
        // de um cliente forjado seria trabalho de graca. O que passar do teto e ignorado, e quem
        // decide se o nome existe mesmo e `FormasDeFrost.Sanear`, no servidor.
        int nf = Math.Min((int)r.GetByte(), Jandirus.Core.Races.FormasDeFrost.Total);
        for (int i = 0; i < nf; i++)
        {
            string corpo = r.GetString(64);
            if (corpo.Length > 0) a.FormasDeFrost.Add(corpo);
        }

        // A COR DA CHAMA, no fim de tudo -- na MESMA ordem do `PutAppearance`. Nulo aqui e legitimo
        // e nao e defeito: quem le DERIVA (ver `Appearance.CorAura`). No caminho de entrada
        // (`CreateChar`) ela e descartada de qualquer jeito, porque quem sorteia e o servidor.
        a.CorAura = r.GetRgb();
        a.CorKi = r.GetRgb();   // na MESMA ordem do `PutAppearance`. Nulo aqui tambem deriva.
        return a;
    }

    /// <summary>
    /// O INVENTARIO INTEIRO, do servidor pro dono. Ele e pequeno (ate 30 pares) e muda pouco, entao
    /// nao vale a pena um pacote de delta: mandar a lista toda quando ela muda e mais simples e nao
    /// tem o modo de falha do delta, que e ficar dessincronizado sem ninguem perceber.
    /// </summary>
    public static void PutInventario(this NetDataWriter w, Jandirus.Core.Items.Inventario inv)
    {
        w.Put((byte)Math.Min(inv.Pilhas.Count, Jandirus.Core.Items.Inventario.Slots));
        for (int i = 0; i < inv.Pilhas.Count && i < Jandirus.Core.Items.Inventario.Slots; i++)
        {
            w.Put(inv.Pilhas[i].Id);
            w.Put((ushort)Math.Clamp(inv.Pilhas[i].Quantidade, 0, ushort.MaxValue));
        }
    }

    public static Jandirus.Core.Items.Inventario GetInventario(this NetDataReader r)
    {
        var inv = new Jandirus.Core.Items.Inventario();
        int n = Math.Min((int)r.GetByte(), Jandirus.Core.Items.Inventario.Slots);
        for (int i = 0; i < n; i++)
        {
            // 64 E NAO 32: o LIVRO DE ENSINAMENTOS carrega os proprios dados NO ID
            // (`Livro|Advanced Targeted Mastery|100`, 35 letras -- ver `LivroDeEnsinamentos`).
            // `GetString(max)` do LiteNetLib devolve VAZIO quando o texto passa do limite -- ele
            // consome os bytes do mesmo jeito, entao o pacote nao se desalinha: o livro
            // simplesmente SUMIA da mochila do dono, sem erro em lugar nenhum.
            string id = r.GetString(64);
            int q = r.GetUShort();
            if (id.Length > 0 && q > 0) inv.Pilhas.Add(new Jandirus.Core.Items.Pilha(id, q));
        }
        return inv;
    }

    public static NetDataWriter Begin(C2S id) { var w = new NetDataWriter(); w.Put((byte)id); return w; }
    public static NetDataWriter Begin(S2C id) { var w = new NetDataWriter(); w.Put((byte)id); return w; }

    // =====================================================================
    // O ESTADO DAS ARVORES NO PACOTE `S2C.Skills`
    // =====================================================================
    /// <summary>
    /// A CAUDA DO `S2C.Skills`: o que o `growbranches()` de cada arvore produziu pra este personagem.
    ///
    /// ============================ POR QUE O RESULTADO, E NAO OS CONTADORES ============================
    /// O cliente rodava `SkillBook.PodeAprender` com um livro que so tinha os paths aprendidos:
    /// `Destravadas` sempre vazio, tier de arvore nenhum, skill acesa nenhuma. Mesmo com o servidor
    /// abrindo a Martial Skill, a tela nao sabia.
    ///
    /// Havia duas saidas: mandar os CONTADORES da ficha (`bodyskill`, `kieffusionskill`, `lssj`...)
    /// e deixar o cliente reavaliar as regras, ou mandar o RESULTADO (arvores abertas, tier de cada
    /// uma, skills acesas e apagadas). Vai o resultado, por tres razoes:
    ///   1. o cliente nao tem `Fighter`. Os contadores sao campos dele, lidos por reflexao pelo nome
    ///      do DM; mandar contadores obrigaria o cliente a criar meia ficha so pra avaliar;
    ///   2. `invested` depende de QUAIS skills foram ensinadas (copia ensinada nao investe,
    ///      `teachable.dm:53-56`), e a marca de ensino e do servidor -- com contadores o cliente
    ///      calcularia um tier diferente do servidor e a tela discordaria do balcao;
    ///   3. o resultado e pequeno (meia duzia de arvores) e e literalmente o que a tela pinta.
    /// O cliente continua rodando o MESMO `SkillBook.Avaliar` do Core sobre esse estado -- a regra
    /// de recusa e uma so; o que muda e de onde vem o estado. A recusa em si NAO viaja: ela e
    /// calculada nas duas pontas pela mesma funcao, e mandar uma segunda copia dela e o jeito de as
    /// duas divergirem.
    /// =================================================================================================
    ///
    /// Layout (depois dos campos antigos do pacote, pra quem ja le nao mudar):
    ///     u16 nDestravadas, string[]                  -- arvores que o progresso abriu
    ///     u16 nArvores; por arvore:
    ///         string path; u8 tier; u16 investido; u16 proximoInvestir; u8 proximoTier;
    ///         u16 nAcesas, string[]; u16 nApagadas, string[]
    ///     u16 nVerbos, string[]                       -- os verbs que este personagem pode acionar HOJE
    ///
    /// ============================ OS VERBS ATIVOS VIAJAM PELO MESMO MOTIVO ============================
    /// O cliente montava os botoes de tecnica so do CATALOGO (`Skill.Verbos` das aprendidas). Os verbs
    /// concedidos por NIVEL (o Hokuto Hyakuretsu Ken no nivel 2 do Hokuto no Shinken -- 60 dos 189 do
    /// jogo) e os por CASA (a Trindade) nao estao em skill nenhuma: estao no `niveis.json` cruzado com o
    /// NIVEL ATUAL, e o nivel e do servidor (decisao de 2026-09-01: ele nao viaja). Entao o servidor
    /// manda o RESULTADO -- a mesma lista que o `SabeTecnica` aceita (`TecnicasDe`) -- e o botao nasce
    /// dela. Sem esta cauda, 16 golpes do lote G10 e 13 do G7 tinham corpo, porta e NENHUM botao.
    /// ================================================================================================
    /// </summary>
    public static void PorEstadoDeSkills(NetDataWriter w, SkillBook livro, IReadOnlyList<string> verbos)
    {
        w.Put((ushort)livro.Destravadas.Count);
        foreach (string p in livro.Destravadas) w.Put(p);

        w.Put((ushort)livro.Arvores.Count);
        foreach (EstadoDeArvore e in livro.Arvores)
        {
            w.Put(e.Path);
            w.Put((byte)Math.Clamp(e.Tier, 0, 255));
            w.Put((ushort)Math.Clamp(e.Investido, 0, ushort.MaxValue));
            w.Put((ushort)Math.Clamp(e.ProximoInvestir, 0, ushort.MaxValue));
            w.Put((byte)Math.Clamp(e.ProximoTier, 0, 255));
            w.Put((ushort)e.Acesas.Count);
            foreach (string p in e.Acesas) w.Put(p);
            w.Put((ushort)e.Apagadas.Count);
            foreach (string p in e.Apagadas) w.Put(p);
        }

        w.Put((ushort)Math.Min(verbos.Count, ushort.MaxValue));
        foreach (string v in verbos) w.Put(v);
    }

    /// <summary>O espelho de <see cref="PorEstadoDeSkills"/> -- o cliente e a bancada leem por aqui.</summary>
    public static (List<string> Destravadas, List<EstadoDeArvore> Arvores, List<string> Verbos) LerEstadoDeSkills(NetDataReader r)
    {
        int nd = r.GetUShort();
        var destravadas = new List<string>(nd);
        for (int i = 0; i < nd; i++) destravadas.Add(r.GetString(96));

        int na = r.GetUShort();
        var arvores = new List<EstadoDeArvore>(na);
        for (int i = 0; i < na; i++)
        {
            var e = new EstadoDeArvore
            {
                Path = r.GetString(96),
                Tier = r.GetByte(),
                Investido = r.GetUShort(),
                ProximoInvestir = r.GetUShort(),
                ProximoTier = r.GetByte(),
            };
            int nac = r.GetUShort();
            for (int k = 0; k < nac; k++) e.Acesas.Add(r.GetString(96));
            int nap = r.GetUShort();
            for (int k = 0; k < nap; k++) e.Apagadas.Add(r.GetString(96));
            arvores.Add(e);
        }

        int nv = r.GetUShort();
        var verbos = new List<string>(nv);
        for (int i = 0; i < nv; i++) verbos.Add(r.GetString(64));
        return (destravadas, arvores, verbos);
    }
}

/// <summary>
/// A ficha viva que o servidor manda de volta pro dono. NAO vai no snapshot: BP alheio se
/// descobre com scouter ou sentido de ki, nunca de graca pelo pacote de estado.
///
/// A CLASSE vem daqui e nao da tela de criacao: o sorteio e do servidor. O cliente e o
/// ultimo a saber qual linhagem saiu, que e o que "sorteio cego" quer dizer.
/// </summary>
public struct SheetState
{
    public string Class;
    public double BP;            // o poder REAL, o que o treino sobe
    public double ExpressedBP;   // o que o mundo le
    public double Ki, MaxKi, HP;

    /// <summary>
    /// VIGOR: o folego. Nao e o STAT (`Rstamina`, que vai na ficha lenta) -- e o valor VIVO, que
    /// cai ao correr, ao socar e ao reunir energia, e volta parado.
    ///
    /// Vai na ficha rapida porque o jogador precisa VER: desde que carregar Ki passou a exigir
    /// folego e a parar sozinho quando ele acaba, ficar sem vigor virou uma coisa que acontece --
    /// e acontecer sem barra e o jogo parar de responder sem dizer por que.
    /// </summary>
    public double Vigor, VigorMax;

    /// <summary>
    /// NUTRICAO: o tanque de comida, que e de onde o vigor VOLTA.
    ///
    /// Vai na ficha rapida pelo mesmo motivo do vigor -- e mais ainda: o vigor cai sozinho e so
    /// sobe as custas disto, entao um jogador com a barra de folego caindo e sem nenhum numero de
    /// comida na tela nao tem como saber que o problema e fome. Ver `Core.Stats.Nutricao`.
    /// </summary>
    public double Nutricao, NutricaoMax;

    /// <summary>
    /// QUAO INTEIRO O CORPO ESTA, de 0 a 1 -- a "% de BP efetivo" ao lado do BP.
    ///
    /// VEM CALCULADA DO SERVIDOR, e tem que vir: ela e `expressedBP / peakexBP`, e sem scouter os
    /// DOIS chegam como <see cref="double.NaN"/> (ver `GameServer.Sigilo`). O cliente nao teria de
    /// onde tirar a razao, e mandar o `peakexBP` cru pra ele fazer a conta seria vazar poder
    /// absoluto pra quem nao tem aparelho. Razao pode; numero nao.
    ///
    /// Quem a define e o Core -- <see cref="Jandirus.Core.Stats.Fighter.Inteireza"/>. Aqui ela so viaja.
    /// </summary>
    public double Inteireza;

    /// <summary>
    /// O MULTIPLICADOR TOTAL sobre o BP base (`expressedBP / BP`), pra aba Forms.
    ///
    /// Mesma licenca de sigilo da <see cref="Inteireza"/>: "x345" nao diz de QUE numero, e o DM e
    /// explicito em nao esconder multiplicador. Ver <see cref="Jandirus.Core.Stats.Fighter.MultiplicadorTotal"/>.
    /// </summary>
    public double MultTotal;

    /// <summary>
    /// ATE ONDE O KI PODE IR CARREGANDO, em RAZAO sobre o <see cref="MaxKi"/> (o `powerupcap`).
    ///
    /// A barra de Ki precisa dele pra ter pra onde crescer: de fabrica o teto e 1,4 (140% do
    /// tanque) e com as skills de power-up no talo passa de 3,8. Sem este numero o trilho teria
    /// que ser 1,0 -- que e exatamente o corte que fazia o HUD mentir acima dos 100%.
    ///
    /// Nunca menor que 1: um teto abaixo do proprio tanque nao existe e viraria divisao esquisita.
    /// </summary>
    public double TetoKi;

    public float SpeedStat;

    /// <summary>
    /// A CADENCIA do soco leve, em milissegundos, ja calculada pelo servidor. Vai na ficha
    /// porque muda com o estado: carregar Ki derruba o `Eactspeed` e o personagem passa a
    /// bater mais rapido. O cliente usa este numero pro proprio cooldown e pra duracao da
    /// animacao, entao o que o jogador ve bate com o que o servidor aceita.
    /// </summary>
    public int SocoMs;

    /// <summary>Quantos dos membros estao quebrados ou decepados -- a UI mostra o boneco.</summary>
    public byte MembrosRuins;

    /// <summary>
    /// O corpo, em bits: 1 = nocauteado, 2 = morto, 4 = guarda erguida, 8 = golpe letal.
    ///
    /// Precisa chegar ao cliente porque e ELE quem move o personagem: sem saber que esta no
    /// chao, o boneco continuaria andando enquanto o servidor recusa cada passo -- a mesma
    /// briga cliente-servidor que fazia o personagem TREMER na parede.
    /// </summary>
    public byte Estado;

    public bool KO => (Estado & 1) != 0;
    public bool Morto => (Estado & 2) != 0;
    public bool Guarda => (Estado & 4) != 0;
    public bool Letal => (Estado & 8) != 0;

    /// <summary>Tenho rabo. O snapshot nao me inclui (eu me desenho sozinho), entao vem aqui.</summary>
    public bool Rabo => (Estado & 16) != 0;

    /// <summary>
    /// ESTOU SENDO ARREMESSADO (o `KB` do original).
    ///
    /// Precisa chegar ao cliente porque o arremesso e a UNICA parte do movimento que o servidor
    /// dirige: quem esta voando nao esta dirigindo. Sem este bit o cliente continuaria integrando
    /// o input e empurrando de volta, e os dois brigariam pelo corpo -- a mesma briga que fazia o
    /// personagem tremer na parede.
    /// </summary>
    public bool Empurrado => (Estado & 32) != 0;

    /// <summary>
    /// O SEGUNDO BYTE DO CORPO. Nasce com um bit usado e sete livres.
    ///
    /// ============================ O PRIMEIRO ENCHEU, E ENCHEU POR CIMA ============================
    /// <see cref="Estado"/> parece ter espaco (KO, morto, guarda, letal, rabo, arremessado = 6 bits),
    /// mas os DOIS DE CIMA ja tem dono: `DirecaoDeitado &lt;&lt; 6` carrega o angulo do corpo caido.
    /// Quem "achasse" o 64 livre apagaria a direcao da queda -- um defeito que so aparece no instante
    /// do nocaute, e que este projeto ja pagou uma vez (o dono fotografou as duas telas com o mesmo
    /// corpo caido pra lados diferentes).
    ///
    /// UM BYTE NOVO NAO E CARO AQUI, e vale dizer por que: a ficha e um pacote PESSOAL de 5 Hz, nao o
    /// snapshot de zona de 30 Hz. Sao 5 bytes por segundo por jogador -- contra os ~110 que o pacote
    /// ja tem em `double`.
    /// =========================================================================================
    /// </summary>
    public byte Estado2;

    /// <summary>
    /// ESTOU NADANDO (o `swim` do original).
    ///
    /// ============================ POR QUE ELE VEM PELA FICHA ============================
    /// Este bit e o irmao do <see cref="Empurrado"/>, e nao por acaso: os dois sao a MESMA especie de
    /// informacao -- **como o meu corpo esta atravessando o mundo** --, e e ela que o cliente precisa
    /// pra prever o passo pela mesma regra que o servidor vai conferir. Ter um vindo pela ficha e o
    /// outro por um canal de evento seria manter duas verdades sobre a mesma pergunta.
    ///
    /// A FICHA E CONTINUA, e e isso que fecha o buraco: um canal de evento ("ligou"/"desligou")
    /// perde o estado se o pacote se perder ou se o servidor desligar o nado por um caminho que
    /// alguem esqueceu de avisar. A ficha reafirma o bit 5 vezes por segundo, e o servidor a manda na
    /// hora quando ele muda (`MandarFicha`) -- o mesmo idioma do arremesso.
    ///
    /// PRA QUEM OLHA DE FORA quem conta e a `Pose.Nadando` do snapshot: um observador nao precisa
    /// prever passo nenhum, precisa desenhar o boneco.
    /// ================================================================================
    /// </summary>
    public bool Nadando => (Estado2 & 1) != 0;

    /// <summary>
    /// ESTOU SENDO PUXADO PRA UMA FUSAO -- o `step_to` do `Potara_Fusion.dm:124-129`.
    ///
    /// ============================ ELE NAO SUBSTITUI O <see cref="Empurrado"/>: ELE O QUALIFICA ============================
    /// Durante o puxao o <see cref="Empurrado"/> tambem esta ligado, e tem que estar -- ele e o bit que
    /// faz o cliente parar de integrar tecla e so deslizar ate a ultima correcao, que e a UNICA coisa
    /// que impede as duas pontas de brigarem pelo mesmo corpo (o tremor que este projeto ja pagou duas
    /// vezes). O que este bit acrescenta e uma coisa so: **o corpo nao gira**.
    ///
    /// O arremesso desenha o corpo DEITADO na direcao do voo (`CharacterVisual.VoarPara`, uma rotacao
    /// de 90 graus por eixo) porque quem foi arremessado esta sendo jogado. Quem esta sendo puxado pra
    /// uma fusao esta de pe e caminhando pro outro -- com a rotacao do arremesso, dois lutadores
    /// atraidos no eixo vertical apareceriam de CABECA PRA BAIXO deslizando um pro outro.
    ///
    /// Um bit e nao um `if` no cliente porque quem sabe por que o corpo esta sendo dirigido e o
    /// SERVIDOR: o cliente nao tem como distinguir um arremesso de um puxao olhando a posicao.
    /// ==========================================================================================================
    /// </summary>
    public bool PuxadoNaFusao => (Estado2 & 2) != 0;

    /// <summary>Nem anda nem golpeia: caido ou morto.</summary>
    public bool Imobilizado => KO || Morto;

    // =====================================================================
    // AS RAZOES, NUM LUGAR SO
    // =====================================================================
    /// <summary>
    /// ============================ UMA FONTE, UMA EXPRESSAO ============================
    /// O Ki em razao do tanque. Parece obvio demais pra virar propriedade, e virou justamente pelo
    /// bug que o dono relatou: o HUD e as duas abas do menu P escreviam `Ki / MaxKi` cada um por
    /// si. A FONTE ja era a mesma (este struct), mas a EXPRESSAO estava copiada em tres lugares --
    /// e o dia em que uma delas passou por um widget que cortava em 100% as tres passaram a
    /// discordar, com a que mente sendo a que o jogador olha a partida inteira.
    ///
    /// Copia de conta e o defeito; ter tres consumidores nao e. Com a conta aqui, mexer nela mexe
    /// nas tres telas ou em nenhuma.
    ///
    /// NAO TEM TETO EM 1: acima do tanque o Ki e linear e vira poder de verdade (Ki a 118% da
    /// 1,18x de BP). Quem quiser desenhar isso usa o <see cref="TetoKi"/> como fim do trilho.
    /// ==================================================================================
    /// </summary>
    public readonly double RazaoDeKi => MaxKi > 0 ? Ki / MaxKi : 0;

    /// <summary>O folego em razao. Mesma regra da <see cref="RazaoDeKi"/>: uma conta, um lugar.</summary>
    public readonly double RazaoDeVigor => VigorMax > 0 ? Vigor / VigorMax : 0;

    /// <summary>O tanque de comida em razao. Mesma regra.</summary>
    public readonly double RazaoDeNutricao => NutricaoMax > 0 ? Nutricao / NutricaoMax : 0;

    /// <summary>O fim do trilho da barra de Ki, em razao. Nunca abaixo de 1 (ver <see cref="TetoKi"/>).</summary>
    public readonly double TrilhoDeKi => Math.Max(TetoKi, 1);

    public void Write(NetDataWriter w)
    {
        w.Put(Class); w.Put(BP); w.Put(ExpressedBP);
        w.Put(Ki); w.Put(MaxKi); w.Put(HP); w.Put(Vigor); w.Put(VigorMax);
        w.Put(Nutricao); w.Put(NutricaoMax);
        w.Put(Inteireza); w.Put(MultTotal); w.Put(TetoKi);
        w.Put(SpeedStat);
        w.Put(SocoMs); w.Put(MembrosRuins); w.Put(Estado); w.Put(Estado2);
    }

    public static SheetState Read(NetDataReader r) => new()
    {
        Class = r.GetString(32), BP = r.GetDouble(), ExpressedBP = r.GetDouble(),
        Ki = r.GetDouble(), MaxKi = r.GetDouble(), HP = r.GetDouble(),
        Vigor = r.GetDouble(), VigorMax = r.GetDouble(),
        Nutricao = r.GetDouble(), NutricaoMax = r.GetDouble(),
        Inteireza = r.GetDouble(), MultTotal = r.GetDouble(), TetoKi = r.GetDouble(),
        SpeedStat = r.GetFloat(),
        SocoMs = r.GetInt(), MembrosRuins = r.GetByte(), Estado = r.GetByte(),
        Estado2 = r.GetByte(),
    };
}

/// <summary>
/// O resumo de um slot, do jeito que a tela de selecao precisa: quem e, e como ele se parece.
/// Vai a aparencia inteira junto porque o retrato do slot e o proprio boneco montado -- e o
/// mesmo desenho que vai andar no mundo, nao uma foto guardada a parte.
/// </summary>
public struct SlotInfo
{
    public bool Ocupado;
    public string Nome, Raca, Classe, Genero;
    public int Idade;
    public double BP;
    public Appearance Visual;

    public void Write(NetDataWriter w)
    {
        w.Put(Ocupado);
        if (!Ocupado) return;
        w.Put(Nome); w.Put(Raca); w.Put(Classe); w.Put(Genero);
        w.Put(Idade); w.Put(BP);
        w.PutAppearance(Visual);
    }

    public static SlotInfo Read(NetDataReader r)
    {
        var s = new SlotInfo { Ocupado = r.GetBool() };
        if (!s.Ocupado) { s.Visual = new Appearance(); return s; }
        s.Nome = r.GetString(24); s.Raca = r.GetString(24);
        s.Classe = r.GetString(32); s.Genero = r.GetString(8);
        s.Idade = r.GetInt(); s.BP = r.GetDouble();
        s.Visual = r.GetAppearance();
        return s;
    }
}

/// <summary>Estado de uma entidade dentro de um snapshot.</summary>
public struct EntityState
{
    public int Id;
    public Vec2 Pos;
    public byte Facing;
    public bool Moving;
    public Protocol.Pose Pose;

    /// ============================ E A VIDA ALHEIA NAO VIAJA MAIS ============================
    /// Havia aqui um `byte Vida` (0-100) que ia pra TODA a zona, todo tique, e alimentava a
    /// barrinha sobre a cabeca. Os dois sairam a pedido do dono: *"n deveria dar pra ver o hp dos
    /// outros, so ter uma ideia com base nos FERIMENTOS"*.
    ///
    /// DIVERGENCIA DELIBERADA DO BYOND -- a barra de vida/Ki sobre a cabeca foi portada de la de
    /// proposito, e agora e retirada de proposito. O que se GANHA: a vida do outro deixa de ser
    /// numero e passa a ser o CORPO dele (hematoma, sangue, rasgo na roupa, membro arrancado), que
    /// e o mesmo sigilo que o BP ja tem sem scouter. O que se PERDE, e e real: nao da mais pra
    /// saber quanto falta pra derrubar alguem -- so "ele esta destrocado".
    ///
    /// APAGAR SO O DESENHO SERIA MEIA SOLUCAO: com o byte no fio a informacao continuaria no jogo
    /// e voltaria na primeira tela que alguem escrevesse. Este port ja pagou essa conta uma vez, no
    /// sigilo do BP, onde escrever o corte nao foi aplicar o corte.
    ///
    /// QUEM CONTA A HISTORIA AGORA e o pacote `S2C.Feridas` (ver `GameServer.Feridas.cs`): 5 bytes
    /// de mascara por regiao + 1 de membros arrancados, GRAU e nao numero, ja mandados pra zona
    /// inteira e so quando mudam. O dono da ficha continua recebendo a PROPRIA vida pelo `S2C.Sheet`
    /// -- a HUD depende disso e nao mudou.
    /// =======================================================================================

    /// <summary>
    /// Este personagem tem RABO agora. Vai num bit que ja estava sobrando no byte de
    /// direcao/pose (o 0x20) -- entao ver o rabo de todo mundo custa ZERO byte a mais.
    /// Precisa vir por tick e nao no pacote de aparencia porque o rabo E ARRANCAVEL: quem
    /// esta olhando tem que ver ele sumir na hora.
    /// </summary>
    public bool Rabo;

    /// <summary>
    /// Este corpo esta OCULTO (a Invisibility do original). Outro bit que sobrava no mesmo
    /// byte (0x40).
    ///
    /// ESCONDER E DO CLIENTE, e o original faz igual -- o `invisibility` do BYOND e filtro de
    /// desenho, o cliente sempre soube a posicao. Filtrar no servidor exigiria um buffer de
    /// snapshot POR DESTINATARIO, e hoje uma zona inteira compartilha um so; pagar isso pra
    /// uma tecnica de duas racas seria caro no lugar errado. Fica anotado como o que e: quem
    /// mexer no cliente ve por baixo do invisivel.
    /// </summary>
    public bool Oculto;

    /// <summary>
    /// ESTA REUNINDO ENERGIA (a tecla C segurada) -- a aura de power-up.
    ///
    /// POR QUE ISTO VAI NO SNAPSHOT e nao pelo canal de `Efeito`. O canal de efeito e PESSOAL
    /// (`MandarEfeito` escreve pro peer de um jogador so), e power-up nao e coisa pessoal: ver o
    /// adversario juntar poder na sua frente e informacao de COMBATE -- e o aviso de que o proximo
    /// golpe vem mais forte, e a deixa pra atacar antes que ele termine. Esconder isso de quem
    /// esta lutando tiraria a leitura da luta.
    ///
    /// PRECISOU DE UM BYTE NOVO porque o de flags encheu: direcao (2 bits) + pose (3) + rabo +
    /// oculto + andando = 8. Este segundo byte nasce com um bit usado e sete livres, e e onde os
    /// proximos estados de zona devem entrar em vez de espremer o primeiro.
    /// </summary>
    public bool Carregando;

    /// <summary>Passou dos 100% de Ki: a aura FECHA, mais forte que a de simples carga.</summary>
    public bool Sobrecarregado;

    /// <summary>Caido ou voando: o corpo desenha DEITADO, e o `Facing` vira a direcao da cabeca.</summary>
    public bool Deitado;

    /// <summary>Correndo de verdade (concedido pelo servidor). Ver <see cref="BitCorrendo"/>.</summary>
    public bool Correndo;

    /// <summary>Este corpo esta VOANDO. Ver <see cref="BitVoando"/>.</summary>
    public bool Voando;

    /// <summary>
    /// ESTE CORPO ESTA SEM REDEAS: quem o dirige e o SERVIDOR, nao quem esta na frente da tela.
    /// Ver <see cref="BitSemRedeas"/>.
    /// </summary>
    public bool SemRedeas;

    /// <summary>Este corpo esta pilotando uma nave. Ver <see cref="BitPilotando"/>.</summary>
    public bool Pilotando;

    /// <summary>
    /// A que altura, em pixels (0 = chao). So chega quando <see cref="Voando"/> -- ver
    /// <see cref="BitVoando"/>.
    /// </summary>
    public float Altitude;

    private const byte BitCarregando = 0x01;
    private const byte BitSobrecarregado = 0x02;

    /// <summary>
    /// ESTE CORPO ESTA DEITADO -- nocauteado ou sendo arremessado.
    ///
    /// PRECISOU VIAJAR NO SNAPSHOT porque o angulo do corpo caido so existia na FICHA, e ficha e
    /// pessoal: quem via o outro cair nunca soube pra que lado ele tinha caido, e quem estava
    /// voando aparecia DE PE pros outros. O dono pegou os dois -- "n ta sincronizando com os
    /// outros clientes".
    ///
    /// A DIRECAO REUSA O CAMPO DE `Facing` que ja esta no pacote: deitado, ele deixa de significar
    /// "pra onde olha" e passa a significar "pra onde a cabeca aponta". Com o corpo em pe as duas
    /// perguntas tem a mesma resposta, entao nao ha byte novo -- so um bit dos seis que sobravam.
    /// </summary>
    private const byte BitDeitado = 0x04;

    /// <summary>
    /// ESTE CORPO ESTA CORRENDO -- e nao andando depressa.
    ///
    /// ============================ POR QUE ISTO NAO SE DEDUZ ============================
    /// O cliente deduzia: media a distancia entre dois snapshots e chamava de corrida tudo acima
    /// de `BaseSpeedPx * SpeedTolerance`. A conta compara com a velocidade BASE do jogo, e nao
    /// com a velocidade DESTE personagem -- que sai do `Espeed` dele e cresce a vida inteira.
    /// Resultado: qualquer um com velocidade alta deixava rastro de corrida ANDANDO, e o dono viu
    /// exatamente isso ("andar mesmo sem apertar shift tava deixando rastro").
    ///
    /// Nao havia como consertar a deducao sem mandar tambem a velocidade do personagem -- que sao
    /// quatro bytes pra dizer o que um bit diz. E correr, aqui, nao e uma leitura de velocidade:
    /// e uma DECISAO que o servidor concede (`GameServer.PodeCorrer`, que cobra Ki por segundo).
    /// O dado existe, e autoritativo, e agora viaja.
    /// ===================================================================================
    /// </summary>
    private const byte BitCorrendo = 0x08;

    /// <summary>
    /// ESTE CORPO ESTA VOANDO -- e o portao do byte de altitude.
    ///
    /// A altura NAO vai sempre. Ela so interessa a quem esta no ar, e quem esta no ar e a minoria
    /// em qualquer instante: cobrar um byte por corpo por snapshot pra dizer "zero" a vida inteira
    /// seria pagar por todo mundo o preco de poucos. Com o bit, quem anda no chao continua custando
    /// exatamente o que custava, e o byte extra so aparece quando ha altura pra contar.
    /// </summary>
    private const byte BitVoando = 0x10;

    /// <summary>
    /// ============================ ESTE CORPO NAO OBEDECE MAIS O DONO ============================
    /// O servidor esta dirigindo (o clone da mente, a fera solta -- qualquer corpo com `Cerebro`).
    ///
    /// POR QUE PRECISOU VIAJAR. Pros corpos ALHEIOS nao precisava: o cliente ja desenha todo mundo
    /// pelo que chega no snapshot, entao o clone e o macaco dos OUTROS sempre andaram animados. O
    /// buraco era o corpo PROPRIO: `World.AoReceberSnapshot` descarta o proprio estado (posicao e
    /// direcao sao previsao local) e quem escolhe a pose e o `LocalPlayer`, medindo o passo que o
    /// TECLADO deu. Quando o servidor passa a dirigir, o teclado nao da passo nenhum -- a posicao
    /// andava e a animacao ficava em `default_<dir>`. E o "sai deslizando" que o dono viu.
    ///
    /// Com este bit o corpo local sabe que virou passageiro e passa a se desenhar pelo que o
    /// servidor manda, como qualquer observador ja fazia.
    ///
    /// POR QUE NO SNAPSHOT E NAO NA FICHA: e ESTADO CONTINUO, reafirmado 30x/s. No quadro em que o
    /// servidor parar de dizer, o cliente esta livre -- nao ha como isto virar a trava permanente
    /// que ja travou jogador neste projeto (ver o portao global no `LocalPlayer`). E como e por
    /// CORPO, ele tambem descreve a fera dos outros de graca.
    /// ============================================================================================
    /// </summary>
    private const byte BitSemRedeas = 0x20;

    /// <summary>
    /// ESTE CORPO ESTA DENTRO DE UMA NAVE -- e por isso ela e desenhada em cima dele.
    ///
    /// ============================ POR QUE UM BIT DE SNAPSHOT E NAO UM PACOTE ============================
    /// A nave PARADA viaja no <see cref="S2C.Construcoes"/>, que e o canal certo pra ela: objeto no
    /// chao, confiavel, so quando muda. A nave PILOTADA nao esta no chao -- ela anda 30 vezes por
    /// segundo, colada num corpo. Manda-la por aquele canal seria reenviar a lista inteira de
    /// construcoes da zona a cada quadro; manda-la num opcode proprio seria um segundo snapshot com
    /// a mesma posicao que ja esta sendo enviada logo ali.
    ///
    /// Um bit resolve porque a posicao ja viaja: o cliente pendura o sprite do pod no corpo que
    /// disse "estou pilotando" (ver `World.MarcarNaNave`), e o DM faz literalmente isso -- o
    /// `verb/Use` copia `loc = locate(pilot.x,pilot.y,pilot.z)` cinco vezes por segundo
    /// (`PlanetTech.dm:140-144`).
    ///
    /// E ELE CABIA: o segundo byte de flags nasceu "com um bit usado e sete livres" (ver
    /// <see cref="Carregando"/>) e ainda tinha dois. Ver alguem passar dentro de uma nave e
    /// informacao do MUNDO, como ver alguem voar -- nao e ficha pessoal.
    /// ================================================================================================
    /// </summary>
    private const byte BitPilotando = 0x40;

    /// <summary>
    /// A NAVE QUE ELE PILOTA E A GRANDE (a Capital Ship), e nao um pod.
    ///
    /// ============================ UM BIT PRA ESCOLHER UM SPRITE ============================
    /// O cliente pendura o desenho da nave no corpo que disse "estou pilotando"
    /// (`World.MarcarNaNave`), e ate a camada 2 so havia um desenho possivel. Agora ha dois -- e
    /// eles nao sao parecidos: a Spacepod e uma capsula de um tile, a Capital Ship tem 128x138 px
    /// (`pixel_x = -48` no `construcoes.json`, vindo do `ShipVessel.dm:74`). Desenhar a capsula
    /// pequena em cima de quem pilota a grande faria a nave de dois milhoes de zeni parecer um pod.
    ///
    /// A ALTERNATIVA seria mandar o CAMINHO DA ARTE, e ela e cara do jeito errado: o snapshot e o
    /// unico pacote que sai 30 vezes por segundo por corpo, e uma string por entidade nele seria
    /// pagar por quadro uma informacao que muda uma vez por embarque.
    ///
    /// ============================ ESTE ERA O ULTIMO BIT LIVRE ============================
    /// O segundo byte de flags nasceu com um bit usado e sete livres (ver <see cref="Carregando"/>);
    /// com este ele fecha. O PROXIMO estado de mundo que precisar viajar no snapshot nao tem onde
    /// entrar, e a saida honesta nao e espremer -- e um terceiro byte, pago so por quem o usa (o
    /// `Altitude` ja e opcional assim: ele so vai no fio quando `Voando` esta ligado).
    /// ==================================================================================
    /// </summary>
    private const byte BitNaveGrande = 0x80;

    /// <summary>A nave em cima dele e a Capital Ship. So faz sentido com <see cref="Pilotando"/>.</summary>
    public bool NaveGrande;

    // =====================================================================
    // O TERCEIRO BYTE -- o que este corpo esta FAZENDO, pra quem esbarra nele
    // =====================================================================
    /// <summary>
    /// ============================ ESTE CORPO ESTA OCUPADO? -- E COM O QUE? ============================
    /// A resposta do <see cref="Jandirus.Core.World.CorpoOcupado"/>, ja calculada pelo servidor. Ela e o
    /// que faz um corpo que esta BATENDO (ou guardando, carregando, canalizando, agarrando...) parar
    /// tambem quem voa -- ver `ClasseDeCorpo.Bloqueia`. E o pedido do dono: *"faca com q n de pra
    /// empurar npcs ou outros players ao andar contra eles enquando eles batem ou fazem outra coisa"*.
    ///
    /// ============================ POR QUE VIAJA A RESPOSTA, E NAO OS SINAIS ============================
    /// A tentacao era deduzir no cliente: <see cref="Pose"/> ja diz Atacando/Canalizando/Treinando/
    /// Meditando/Nocauteado e o bit <see cref="Carregando"/> diz a tecla C. Sao SEIS dos dez estados --
    /// e os quatro que faltam (guarda, agarrao, embate, cinematica) nao tem bit nenhum no fio.
    ///
    /// Deduzir os seis e mandar os quatro seria escrever a lista DUAS vezes, uma em cada ponta, e o
    /// dia em que um estado novo entrasse so numa delas o sintoma seria "so acontece com os outros".
    /// Aqui a lista existe num lugar so (o `CorpoOcupado`), o servidor a resolve uma vez por corpo por
    /// tique e o cliente **le a resposta**.
    ///
    /// ============================ E POR QUE UM BYTE INTEIRO, TODO TIQUE ============================
    /// Os dois bytes de flags acabaram, e o comentario do <see cref="BitNaveGrande"/> ja tinha escrito
    /// o que fazer quando isso acontecesse: *"a saida honesta nao e espremer -- e um terceiro byte,
    /// pago so por quem o usa"*. **Este e aquele terceiro byte, e ele NAO consegue ser opcional**, e a
    /// razao merece estar escrita: os dois opcionais que existem (<see cref="Altitude"/> e
    /// <see cref="Canal"/>) sao portados por um bit que ja viajava. Aqui nao sobrou bit nenhum pra
    /// servir de porta -- e uma porta que dissesse "estou ocupado" ja seria a informacao inteira.
    ///
    /// Custo: 1 byte por corpo por tique, ~7% do <see cref="EntityState"/>, e ele carrega 10 estados
    /// nos 4 bits de baixo com 4 bits e seis valores livres pro proximo. A alternativa -- perguntar ao
    /// servidor a cada esbarrao -- nao existe: o andador precisa PARAR no quadro, e nao um `ping`
    /// depois (parar so no servidor seria correcao em jogo honesto, o corpo tremendo).
    /// ==============================================================================================
    /// </summary>
    public Jandirus.Core.World.Ocupacao Ocupacao;

    /// <summary>
    /// Os 4 bits de baixo do terceiro byte. Os 4 de cima ficam livres -- e e onde o proximo estado de
    /// mundo entra, em vez de espremer os dois primeiros bytes (ver <see cref="Ocupacao"/>).
    /// </summary>
    private const byte MascaraDaOcupacao = 0x0F;

    // =====================================================================
    // O CANAL DE KI -- um byte OPCIONAL, e so pra quem esta com um raio na mao
    // =====================================================================
    /// <summary>
    /// A FASE E O DESENHO DA CARGA DE QUEM ESTA CANALIZANDO KI. **So vai no fio quando
    /// <see cref="Pose"/> e <see cref="Protocol.Pose.Canalizando"/>.**
    ///
    /// ============================ POR QUE UM BYTE, E POR QUE OPCIONAL ============================
    /// Os dois bytes de flags acabaram (ver <see cref="BitNaveGrande"/> logo acima, que fechou o
    /// segundo), e o <see cref="Protocol.Pose"/> fechou com o `Canalizando`. Este e o "terceiro
    /// byte" que aquele comentario propoe -- e ele foi cobrado como aquele comentario manda: **pago
    /// so por quem o usa**, exatamente como o <see cref="Altitude"/>, que so viaja com `Voando`
    /// ligado.
    ///
    /// CUSTO REAL: 1 byte por corpo QUE ESTA CANALIZANDO por tique. Numa zona com 151 habitantes e
    /// ninguem atirando, custa ZERO -- que e o caso normal. Um raio de pe custa 30 B/s.
    ///
    /// ============================ E POR QUE ELE NAO PODIA SER DERIVADO ============================
    /// A tentacao foi deduzir a fase do que ja viaja. Nao da, e as duas tentativas falham por razoes
    /// diferentes, entao ficam escritas:
    ///
    ///   * PELO PROJETIL: o <see cref="ProjetilState"/> do snapshot nao carrega DONO (so id, posicao,
    ///     tipo e cauda), e o dono so vai no `NascimentoDeProjetil`, uma vez. Pior: as duas pontas da
    ///     janela nao batem -- a CARGA acontece antes de o projetil existir, e fechar o canal **nao
    ///     mata o raio** (`FecharCanal` so para de alimenta-lo). A pose ficaria pendurada depois de o
    ///     jogador soltar, que e o defeito ao contrario.
    ///
    ///   * PELO `Moving`: quem canaliza tem `Moving` sempre falso (o `PodeMexerOCorpo` recusa o
    ///     passo), entao o bit estaria "livre" enquanto esta pose vale. Isso e espremer: seria um
    ///     segundo significado escondido num campo cujo nome diz outra coisa, e a primeira pessoa a
    ///     deixar um corpo canalizar andando (uma investida? um arrasto?) quebraria o desenho sem
    ///     nada apontando pra ca.
    /// ==========================================================================================
    /// </summary>
    public byte Canal;

    /// <summary>O raio JA ESTA SAINDO da mao (o `beaming` do DM). Falso = ainda reunindo (`charging`).</summary>
    public bool CanalAtirando
    {
        get => (Canal & 0x01) != 0;
        set => Canal = (byte)(value ? Canal | 0x01 : Canal & ~0x01);
    }

    /// <summary>
    /// QUAL DOS NOVE DESENHOS DE `BlastCharges` este corpo acende -- o `ChargeState` do DM
    /// (`mobhandler.dm:7`). De 1 a 9; zero quer dizer "nao mandado". Ver `ArteDeProjetil.CargaDeRaio`.
    ///
    /// Cabe nos 4 bits de cima porque 9 cabe em 4 bits. Ele viaja mesmo na fase de TIRO (em que
    /// ninguem o desenha) porque tira-lo de la nao economizaria byte nenhum -- o byte ja esta no
    /// pacote -- e custaria um caso especial em duas pontas.
    /// </summary>
    public int CargaDoCanal
    {
        get => (Canal >> 1) & 0x0F;
        set => Canal = (byte)((Canal & 0x01) | ((value & 0x0F) << 1));
    }

    public void Write(NetDataWriter w)
    {
        w.Put(Id);
        w.PutVec(Pos);
        // direcao (2 bits) + pose (3 bits) + "andando" (1 bit) no MESMO byte
        w.Put((byte)((Facing & 0x03) | ((byte)Pose & 0x07) << 2
                   | (Rabo ? 0x20 : 0x00) | (Oculto ? 0x40 : 0x00)
                   | (Moving ? 0x80 : 0x00)));
        w.Put((byte)((Carregando ? BitCarregando : 0) | (Sobrecarregado ? BitSobrecarregado : 0)
                   | (Deitado ? BitDeitado : 0) | (Correndo ? BitCorrendo : 0)
                   | (Voando ? BitVoando : 0) | (SemRedeas ? BitSemRedeas : 0)
                   | (Pilotando ? BitPilotando : 0)
                   | (NaveGrande ? BitNaveGrande : 0)));
        // O TERCEIRO BYTE: o que este corpo esta FAZENDO. Ver `Ocupacao` -- ele e o unico do pacote
        // que nao e opcional porque nao sobrou bit nenhum pra servir de porta.
        w.Put((byte)((byte)Ocupacao & MascaraDaOcupacao));
        if (Voando) w.Put(Jandirus.Core.World.Voo.ParaByte(Altitude));
        // O BYTE DO CANAL, e so pra quem esta canalizando -- ver `Canal`. A ORDEM importa e e a
        // mesma na leitura: altitude primeiro (ela e a mais antiga), canal depois.
        if (Pose == Protocol.Pose.Canalizando) w.Put(Canal);
    }

    public static EntityState Read(NetDataReader r)
    {
        var e = new EntityState { Id = r.GetInt(), Pos = r.GetVec() };
        byte flags = r.GetByte();
        e.Facing = (byte)(flags & 0x03);
        e.Pose = (Protocol.Pose)((flags >> 2) & 0x07);
        e.Rabo = (flags & 0x20) != 0;
        e.Oculto = (flags & 0x40) != 0;
        e.Moving = (flags & 0x80) != 0;
        byte flags2 = r.GetByte();
        e.Carregando = (flags2 & BitCarregando) != 0;
        e.Sobrecarregado = (flags2 & BitSobrecarregado) != 0;
        e.Deitado = (flags2 & BitDeitado) != 0;
        e.Correndo = (flags2 & BitCorrendo) != 0;
        e.Voando = (flags2 & BitVoando) != 0;
        e.SemRedeas = (flags2 & BitSemRedeas) != 0;
        e.Pilotando = (flags2 & BitPilotando) != 0;
        e.NaveGrande = (flags2 & BitNaveGrande) != 0;
        // A ORDEM E A MESMA DA ESCRITA: ocupacao, altitude, canal. Um valor que este binario nao
        // conhece vira `Livre` em vez de virar estado inventado -- ver `CorpoOcupado.DeByte`.
        e.Ocupacao = Jandirus.Core.World.CorpoOcupado.DeByte((byte)(r.GetByte() & MascaraDaOcupacao));
        if (e.Voando) e.Altitude = Jandirus.Core.World.Voo.DeByte(r.GetByte());
        if (e.Pose == Protocol.Pose.Canalizando) e.Canal = r.GetByte();
        return e;
    }
}

/// <summary>
/// UM ATAQUE DE KI VIVO, dentro do snapshot -- o segundo bloco, depois dos corpos.
///
/// ============================ POR QUE ELE NAO E UM `EntityState` ============================
/// A tentacao era obvia: o snapshot ja carrega entidades, bastaria um valor novo de `Pose`. So que
/// o <see cref="EntityState"/> descreve CORPO -- rabo, oculto, carregando, sobrecarregado,
/// deitado, correndo, voando, sem-redeas: oito bits que um raio nunca vai ter --, e um projetil
/// precisa de duas coisas que corpo nenhum precisa: a COR (cada um pinta o seu ki) e o FIM DO
/// RASTRO (um beam e um segmento, nao um ponto). Enfiar os dois num campo de corpo faria todo corpo
/// da zona pagar por eles.
///
/// Sao 15 bytes por bola e 23 por raio, e so quando ha tiro no ar. Uma zona em paz escreve o
/// contador zero: DOIS bytes.
/// ===========================================================================================
/// </summary>
public struct ProjetilState
{
    public int Id;

    /// <summary>A CABECA -- a unica parte que acerta, e por isso a unica posicao autoritativa.</summary>
    public Vec2 Pos;

    /// <summary>O tipo (`Core.Combat.TipoDeProjetil`) nos dois bits baixos.</summary>
    public byte Tipo;

    /// <summary>
    /// O FIM DO RASTRO. So viaja pro <c>Beam</c> -- ver <see cref="BitTemCauda"/>. Uma bola nao tem
    /// rastro, e mandar oito bytes de "igual a cabeca" pra cada bola no ar seria pagar o preco do
    /// raio em todo tiro.
    /// </summary>
    public Vec2 Cauda;

    private const byte MascaraDoTipo = 0x03;
    private const byte BitTemCauda = 0x04;

    public void Write(NetDataWriter w)
    {
        w.Put(Id);
        w.PutVec(Pos);
        bool cauda = (Tipo & MascaraDoTipo) == (byte)Jandirus.Core.Combat.TipoDeProjetil.Beam;
        w.Put((byte)((Tipo & MascaraDoTipo) | (cauda ? BitTemCauda : 0)));
        if (cauda) w.PutVec(Cauda);
    }

    public static ProjetilState Read(NetDataReader r)
    {
        var p = new ProjetilState { Id = r.GetInt(), Pos = r.GetVec() };
        byte flags = r.GetByte();
        p.Tipo = (byte)(flags & MascaraDoTipo);
        p.Cauda = (flags & BitTemCauda) != 0 ? r.GetVec() : p.Pos;
        return p;
    }
}

/// <summary>
/// UM TIRO ACABOU DE NASCER -- tudo que so e dito UMA vez, no canal confiavel.
///
/// ============================ POR QUE UM TIPO E NAO SEIS PARAMETROS ============================
/// O evento era `Action&lt;int, int, byte, Vec2&gt;` e crescer pra `&lt;int, int, byte, ushort, byte,
/// Vec2&gt;` poria DOIS pares de tipos iguais lado a lado (`int,int` e `byte,...,byte`): trocar dois
/// argumentos de posicao continuaria compilando e o defeito sairia como "o tiro do fulano tem a
/// arte do sicrano". Nomeando os campos, o compilador volta a ser quem confere.
///
/// NAO E `ProjetilState`, e nao pode ser: aquele e o estado CONTINUO, 30 Hz, e o que ele nao carrega
/// e exatamente o que este carrega -- ver o bloco de <see cref="Protocol.ProjetilSub.Nasceu"/>.
/// =========================================================================================
/// </summary>
public readonly record struct NascimentoDeProjetil(
    int Id,
    /// <summary>Quem atirou. E dele que sai a COR -- ela nunca viaja no pacote.</summary>
    int Dono,
    /// <summary>`Core.Combat.TipoDeProjetil`: como o tiro se comporta.</summary>
    byte Tipo,
    /// <summary>`Core.Combat.ArteDeKi`: qual folha ele desenha. Zero = nenhuma, cai na primitiva.</summary>
    ushort Arte,
    /// <summary>A escala do sprite, ja em multiplicador. Ver `Protocol.DeEscalaDeProjetil`.</summary>
    float Escala,
    /// <summary>
    /// A QUE ALTURA ELE VOA, em pixels de mundo -- a do dono no instante do disparo.
    ///
    /// Ela viaja AQUI e nao no <see cref="ProjetilState"/> porque nao muda depois do nascimento:
    /// e um byte por disparo em vez de um byte por tiro por tique. Sem ela o cliente desenhava
    /// todo tiro no plano do chao, e o feixe de quem estava voando alto nascia ate 160 px abaixo
    /// do proprio corpo -- na sombra.
    /// </summary>
    float Altitude,
    Vec2 Pos);

/// <summary>
/// A SERIALIZACAO DE UMA TECNICA INVENTADA -- as duas pontas, no MESMO arquivo.
///
/// ============================ POR QUE AQUI E NAO EM CADA LADO ============================
/// O objeto que viaja e o proprio <see cref="TecnicaCustomizada"/> do `Core`: o cliente nao tem uma
/// cópia-de-tela do modelo, ele recebe o modelo. Faltava so a ordem dos bytes, e ela e o classico
/// lugar de divergir -- um campo novo escrito de um lado e nao lido do outro desalinha TUDO que vem
/// depois, e o sintoma aparece num campo que ninguem tocou.
///
/// Escrita e leitura coladas uma na outra sao a unica defesa barata: quem acrescentar um campo ve
/// as duas metades na mesma tela.
///
/// O CLIENTE RECEBER O MODELO NAO O DEIXA DECIDIR NADA: `Aplicar` nunca e chamado la (ver
/// `TelaDeTecnicas`), e o servidor devolve a mesa inteira depois de cada compra. O modelo viaja
/// porque e o que a TELA precisa MOSTRAR -- e mostrar `Gasto` calculado do lado errado e como um
/// jogo passa a discordar de si mesmo.
/// ====================================================================================
/// </summary>
public static class CustomWire
{
    private const byte BitDizGrito = 1 << 0;
    private const byte BitDizGritoDeCarga = 1 << 1;
    private const byte BitUsaStamina = 1 << 2;
    private const byte BitCarregavel = 1 << 3;
    private const byte BitInstantaneo = 1 << 4;
    private const byte BitCriada = 1 << 5;

    public const int MaxNome = 32;
    public const int MaxDesc = 160;
    public const int MaxGrito = 48;

    public static void Escrever(NetDataWriter w, TecnicaCustomizada t)
    {
        w.Put((byte)t.Id);
        w.Put((byte)t.Tipo);
        w.Put(t.Nome);
        w.Put(t.Desc);
        w.Put(t.Grito);
        w.Put(t.GritoDeCarga);
        w.Put((byte)((t.DizGrito ? BitDizGrito : 0)
                   | (t.DizGritoDeCarga ? BitDizGritoDeCarga : 0)
                   | (t.UsaStamina ? BitUsaStamina : 0)
                   | (t.Carregavel ? BitCarregavel : 0)
                   | (t.Instantaneo ? BitInstantaneo : 0)
                   | (t.Criada ? BitCriada : 0)));
        w.Put((float)t.BaseDano);
        w.Put((float)t.CargaMinima);
        w.Put((float)t.CustoKi);
        w.Put((float)t.CustoStamina);
        w.Put((float)t.Velocidade);
        w.Put((float)t.Alcance);
        w.Put((float)t.DistanciaMod);
        // COM SINAL, e nao byte -- e agora e o LEITOR que faz esse valor caber. `Gasto` vive em 0..5
        // desde que o dono poz piso em zero (ver `TecnicaCustomizada.Gasto`), entao um byte bastaria;
        // o `short` fica porque um pacote velho ou adulterado PODE trazer negativo, e um byte sem
        // sinal transformaria -5 em 251 caladamente. Ver o `RestaurarGasto` do lado da leitura.
        w.Put((short)t.Gasto);

        // A ARTE ESCOLHIDA. Vai NO FIM, e nao junto do `Tipo` onde ficaria "organizada": o leitor e
        // o escritor deste struct sao lidos lado a lado e crescer pelo fim e o que mantem os dois
        // triviais de conferir. Ver `TecnicaCustomizada.Arte`.
        w.Put((ushort)t.Arte);
    }

    public static TecnicaCustomizada Ler(NetDataReader r)
    {
        var t = new TecnicaCustomizada
        {
            Id = r.GetByte(),
            Tipo = (Jandirus.Core.Combat.TipoDeProjetil)r.GetByte(),
            Nome = r.GetString(MaxNome),
            Desc = r.GetString(MaxDesc),
            Grito = r.GetString(MaxGrito),
            GritoDeCarga = r.GetString(MaxGrito),
        };
        byte f = r.GetByte();
        t.DizGrito = (f & BitDizGrito) != 0;
        t.DizGritoDeCarga = (f & BitDizGritoDeCarga) != 0;
        t.UsaStamina = (f & BitUsaStamina) != 0;
        t.Carregavel = (f & BitCarregavel) != 0;
        t.Instantaneo = (f & BitInstantaneo) != 0;
        t.Criada = (f & BitCriada) != 0;
        t.BaseDano = r.GetFloat();
        t.CargaMinima = r.GetFloat();
        t.CustoKi = r.GetFloat();
        t.CustoStamina = r.GetFloat();
        t.Velocidade = r.GetFloat();
        t.Alcance = r.GetFloat();
        t.DistanciaMod = r.GetFloat();
        // GRAMPEADO NA ENTRADA. `Gasto` nao tem `set` publico de proposito: quem compra passa pelo
        // funil do `Core`, e quem RECONSTROI (fio e disco) entra por aqui, que prende em 0..5. Um
        // save velho com saldo negativo -- legitimo antes do piso -- daria orcamento inflado se
        // entrasse cru.
        t.RestaurarGasto(r.GetShort());
        t.Arte = (Jandirus.Core.Combat.ArteDeKi)r.GetUShort();
        return t;
    }
}
