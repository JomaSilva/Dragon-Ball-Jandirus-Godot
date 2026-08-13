using Godot;

namespace Jandirus.Client;

/// <summary>
/// A LUZ QUE UM PERSONAGEM EMITE. Desligada por padrao, e essa e a regra:
///
///   personagem comum NAO BRILHA. Quem brilha e quem tem CHAMA acesa.
///
/// Antes cada jogador carregava um <see cref="PointLight2D"/> fixo -- todo mundo andava com
/// uma lanterna colada no corpo, o que apagava a diferenca entre um lutador qualquer e um
/// Super Saiyajin aceso. O mundo agora e claro por conta propria (o ambiente e dia) e esta
/// luz volta a significar alguma coisa.
///
/// A regra dizia "quem esta TRANSFORMADO" ate a forma parar de acender sozinha. Hoje quem puxa
/// o gatilho e a carga (tecla C) ou a sobrecarga de Ki, e a luz segue a chama que estiver
/// desenhada -- seja o desenho DESTE node, seja o da <see cref="CargaVisual"/>, que avisa por
/// <see cref="ChamaDaCarga"/>. A `PointLight2D` continua sendo UMA so, e mora aqui.
///
/// ============================ MAS SO HA AURA SE HOUVER FORMA ============================
/// ESTE NODE E DA FORMA. Na base ele nao entra em jogo: nem desenho, nem luz, por mais Ki que o
/// jogador tenha. Quem fica ativo na base e so a <see cref="CargaVisual"/> -- e ela nao tem luz
/// nenhuma, entao a base simplesmente NAO ILUMINA. Ver a guarda em <see cref="Aplicar"/>.
/// ======================================================================================
///
/// O node existe sempre e nasce DESLIGADO: acender e trocar de cor tem que ser instantaneo
/// no meio de uma transformacao, e criar o PointLight2D na hora custaria um engasgo bem no
/// quadro em que o jogador esta olhando. E ele TEM que existir na base, apesar da guarda acima:
/// e aqui que mora a unica resposta pra "de que cor e a chama deste corpo"
/// (<see cref="CorDaChama"/>), que a `CargaVisual` LE pra pintar a chama da base.
/// </summary>
public partial class Aura : Node2D
{
    /// <summary>Raio da aura em pixels. Cinco tiles: da pra ver de longe que alguem acendeu.</summary>
    private const int Raio = 160;

    /// <summary>
    /// ============================ A COR DO KI CRU -- O FALLBACK DA CHAMA PESSOAL ============================
    /// O branco-azulado que todo mundo tem antes de qualquer forma. Ela mora AQUI, junto de
    /// <see cref="_corAcesa"/>, porque este node e o dono da resposta pra "de que cor e a chama
    /// deste corpo" -- e a base e so mais um caso dessa mesma pergunta, nao um assunto da tecla C.
    /// (Ela morava na `CargaVisual` com o nome `CorCarga`, de quando a carga tinha cor propria.)
    ///
    /// ============================ ELA DEIXOU DE SER A CHAMA DE TODO MUNDO ============================
    /// Era: a chama da base era ESTA cor, igual pra todo personagem do servidor. Hoje cada corpo tem
    /// a propria (<see cref="CorPessoal"/>, sorteada no nascimento como no `CharacterCreation.dm:25-27`
    /// e entregue no `PeerLook`), e esta constante ficou sendo o que ela sempre foi por baixo: **a cor
    /// de quem ainda nao tem ficha**. Ha exatamente tres casos vivos, e nenhum deles e um jogador
    /// desenhado por inteiro:
    ///
    ///   1. o corpo que nasceu do snapshot e cujo `PeerLook` ainda nao chegou (canais diferentes, sem
    ///      ordem garantida -- ver `World.VestirCorpoInteiro`). Ele fica com esta cor por alguns quadros;
    ///   2. a `CargaVisual` de um corpo montado pela metade, que ainda nao tem o node `Aura` irmao
    ///      (`CargaVisual.Pintar`: `aura?.CorDaChama ?? CorDoKiCru`);
    ///   3. a bancada, que monta nodes soltos sem servidor nenhum.
    ///
    /// E O PADRAO DE <see cref="_corPessoal"/> por isso, e nao por conveniencia: um corpo que ainda
    /// nao recebeu forma nenhuma ESTA na base, e a cor dele tem que ser ALGUMA desde o primeiro quadro.
    /// Com o antigo padrao `Colors.White` um remoto que entrasse na zona e segurasse C antes do
    /// primeiro `PrepararAuraDaForma` desenharia a chama em branco -- e branco multiplicando a folha
    /// colorivel APAGA a arte (defeito ja pago uma vez). Esta cor nunca e branca, e e por isso que ela
    /// continua sendo o fallback certo.
    ///
    /// A entrada `base` do catalogo NAO serve pra isso: o `Aura` dela e `ffffff`, exatamente o
    /// branco que apaga. Ver `World.PrepararAuraDaForma`, que escreve esta constante no lugar dela.
    /// ==============================================================================================
    /// </summary>
    public static readonly Color CorDoKiCru = new(0.62f, 0.80f, 1.0f);

    /// <summary>
    /// ============================ A COR DA CHAMA **DESTE** CORPO ============================
    /// A cor sorteada no nascimento do personagem (`Appearance.CorAura`), que chega pelo `PeerLook`
    /// e e escrita aqui pelo `World.VestirCorpoInteiro`. Ela e o que a base acende, e o que o
    /// Mistico acende (ver `Catalogo.ChamaDoJogador`).
    ///
    /// MORA NO NODE, E NAO NUM MAPA GLOBAL, por uma razao so: as tres chamas de um corpo (esta, a da
    /// <see cref="CargaVisual"/> e a da cinematica) ja saem daqui. Um mapa `id -> cor` a parte seria
    /// uma quarta resposta pra "de que cor e a chama deste corpo", e este arquivo inteiro e a
    /// historia de quando havia duas.
    /// ======================================================================================
    /// </summary>
    public Color CorPessoal => _corPessoal;

    private Color _corPessoal = CorDoKiCru;

    /// <summary>
    /// A COR DA CHAMA QUE UMA FICHA DESCREVE. Nulo no campo = ficha de antes desta funcionalidade
    /// que ainda nao passou pelo servidor; ai vale o <see cref="CorDoKiCru"/>, e o cabecalho dele
    /// diz por que isso e legitimo.
    ///
    /// EM JOGO O NULO NAO ACONTECE: quem manda a ficha e o servidor, e o `AccountStore.ParaJogador`
    /// DERIVA a cor de todo save que nao a tenha antes de o corpo existir. Este ramo e pra a
    /// bancada e pra o dia em que alguem montar uma `Appearance` na mao.
    ///
    /// A CONVERSAO E CRUA (canal/255) e nao passa por `Color.FromHtml`: a `Rgb` ja e 8 bits por
    /// canal e o hexa no meio do caminho so seria uma chance de arredondar diferente.
    /// </summary>
    public static Color CorPessoalDe(Jandirus.Core.Appearance.Appearance ap) =>
        ap.CorAura is { } c ? new Color(c.R / 255f, c.G / 255f, c.B / 255f) : CorDoKiCru;

    /// <summary>
    /// A ficha visual deste corpo chegou (ou mudou). Ver <see cref="CorPessoal"/>.
    ///
    /// REPINTA O QUE JA ESTA ACESO, e so quando a chama de agora e a PESSOAL -- um Super Saiyajin
    /// nao troca de dourado porque a ficha do dono chegou atrasada. Quem sabe se a chama e pessoal
    /// e o <see cref="_chamaEhPessoal"/>, escrito por quem VESTE (<see cref="Preparar"/>), e nao
    /// deduzido comparando cores: deduzir pela cor e o erro que o `<param name="temForma">` do
    /// `Preparar` ja documenta em outro contexto (a base e a cinematica do Oozaru acendem a MESMA
    /// cor de proposito).
    ///
    /// E O CAMINHO COMUM PASSA POR AQUI, nao pelo `Preparar`: um corpo na BASE nunca chama
    /// `PrepararAuraDaForma` (nao ha forma), entao sem esta linha a chama pessoal so apareceria na
    /// primeira transformacao -- e voltaria a aparecer errada em todo mundo que nunca se transforma.
    /// </summary>
    public void DefinirCorPessoal(Color c)
    {
        if (_corPessoal.IsEqualApprox(c)) return;
        _corPessoal = c;
        if (!_chamaEhPessoal) return;
        _corAcesa = c;
        if (_acesa || _cargaAtiva) Aplicar();
    }

    /// <summary>
    /// A chama guardada agora e a do JOGADOR (base, Mistico) ou a da FORMA (todo o resto)?
    ///
    /// Nasce VERDADEIRO porque um corpo sem forma esta na base, e a base e pessoal. Quem o escreve
    /// e o <see cref="Preparar"/>, com a resposta do Core (`Catalogo.ChamaDoJogador`) -- este node
    /// nao repete a regra, so guarda de quem era a cor que ele acabou de receber.
    /// </summary>
    private bool _chamaEhPessoal = true;

    /// <summary>
    /// ============================ A CHAMA DE UMA FORMA: UMA CONTA, TRES DESENHOS ============================
    /// Estas duas linhas estavam escritas por extenso no `World.PrepararAuraDaForma` E no
    /// `Transformacao.Vestir`, e a chama da cinematica seria a terceira copia. Duas copias ja bastam
    /// pra divergir: quem mexesse no realce de uma forma nova acertaria uma e deixaria a outra pra tras,
    /// e o sintoma seria "a aura da cena e de um tom e a do corpo e de outro" -- que e exatamente a
    /// familia de defeito que a <see cref="CorDaChama"/> existe pra fechar.
    ///
    /// NULO **E** A BASE, e o `Id == base` tambem: o `World` normaliza a entrada `base` pra nulo antes de
    /// chamar, e a cinematica veste a base como degrau de verdade (o primeiro da escada do SSJ3). As duas
    /// portas tem que dar a mesma resposta, senao a base tem duas cores.
    ///
    /// E A BASE NAO E O `Aura` DA ENTRADA `base` DO CATALOGO, que e `ffffff`: branco multiplicando a
    /// folha colorivel APAGA a arte (defeito ja pago uma vez -- ver <see cref="CorDoKiCru"/>).
    /// ====================================================================================================
    ///
    /// ============================ E A BASE DEIXOU DE SER A UNICA CHAMA PESSOAL ============================
    /// Esta conta perguntava `EhBase`, e isso bastava enquanto "usar a cor do jogador" e "nao estar
    /// transformado" fossem a mesma coisa. Deixaram de ser: o dono pediu que o **Mistico** acenda a
    /// chama do personagem -- *"a aura do mistico tem q ser a mesma aura da BASE DO PERSONAGEM, porem
    /// com os efeitos de raiozinhos q ja existem"*.
    ///
    /// QUEM RESPONDE E O CORE (`Catalogo.ChamaDoJogador`), pela mesma razao que a folha e a cor do
    /// contorno ja moram la: um `if (d.Id == "mistico")` aqui seria uma segunda descricao da mesma
    /// regra, e este arquivo ja pagou essa familia de defeito tres vezes (a chama da cinematica, a da
    /// carga e a do corpo sao TRES desenhos da mesma pergunta).
    ///
    /// A FORCA CONTINUA PERGUNTANDO `EhBase`, e a assimetria e a regra: o Mistico usa a COR do jogador
    /// mas e uma transformacao de 16x -- a chama dele e a mesma cor, mais densa. Unificar as duas
    /// perguntas apagaria justamente o que separa "estou na base" de "estou Mistico".
    /// ====================================================================================================
    ///
    /// ============================ E A COR DO JOGADOR VIROU ARGUMENTO ============================
    /// Ela era o <see cref="CorDoKiCru"/> -- uma constante, a mesma pra todo mundo. Deixou de ser: cada
    /// personagem sorteia a propria no nascimento (ver <see cref="CorPessoal"/>), entao a resposta desta
    /// funcao depende de QUEM esta acendendo, e nao so de QUE forma esta vestida.
    ///
    /// SEM SOBRECARGA DE UM ARGUMENTO SO, de proposito. Uma versao "sem o jogador" com o fallback por
    /// dentro compilaria em todo chamador antigo e devolveria a cor de outra pessoa -- calada. Sendo
    /// obrigatoria, foi o compilador quem listou os quatro pontos do jogo que precisavam saber disso.
    /// ==========================================================================================
    /// </summary>
    /// <param name="pessoal">
    /// A cor DESTE corpo (<see cref="CorPessoal"/>). Quem nao tem um corpo em maos passa o
    /// <see cref="CorDoKiCru"/>, e o cabecalho daquela constante lista os tres casos em que isso e
    /// legitimo.
    /// </param>
    public static Color CorDaChamaDe(Jandirus.Core.Forms.FormaDef? d, Color pessoal) =>
        Jandirus.Core.Forms.Catalogo.ChamaDoJogador(d) ? pessoal : new Color(d!.Aura);

    /// <inheritdoc cref="CorDaChamaDe"/>
    public static float ForcaDaChamaDe(Jandirus.Core.Forms.FormaDef? d) =>
        EhBase(d) ? 1f : 0.8f + d!.Intensidade * 0.5f;

    /// <summary>Ver <see cref="CorDaChamaDe"/>: sem forma e "vestindo a base" sao o mesmo estado.</summary>
    private static bool EhBase(Jandirus.Core.Forms.FormaDef? d) =>
        d == null || d.Id == Jandirus.Core.Forms.Catalogo.IdBase;

    private PointLight2D _luz = null!;

    /// <summary>
    /// O DESENHO da aura -- o `colorablebigaura` do original, tingido com a cor da forma.
    ///
    /// A LUZ SOZINHA NAO E A AURA. Ela ilumina o cenario em volta, mas nao poe nada em volta do
    /// CORPO: o Super Saiyajin ficava com o chao dourado e sem os fachos de energia que sao a
    /// imagem da transformacao. O sprite e a aura; a luz e o que ela faz no mundo -- e por isso as
    /// duas saem sempre do mesmo estado, no <see cref="Aplicar"/>.
    /// </summary>
    private SpriteDeAura _desenho = null!;

    /// <summary>O desenho da chama. Pra bancada medir a ancora -- ver `SpriteDeAura.BaseDeTeste`.</summary>
    public SpriteDeAura DesenhoDeTeste => _desenho;

    public override void _Ready()
    {
        _desenho = new SpriteDeAura { Name = "Desenho" };
        AddChild(_desenho);

        _luz = new PointLight2D
        {
            Name = "Luz",
            Texture = Radial(Raio),
            Enabled = false,
            ShadowEnabled = true,
            Energy = 0,
            // ATRAS do personagem: uma luz por cima lava o sprite e apaga o desenho da roupa
            ZIndex = -5,
        };
        AddChild(_luz);
    }

    /// <summary>
    /// Acende (ou apaga) a aura. <paramref name="forca"/> 0 apaga; 1 e uma forma comum;
    /// acima disso a luz cresce junto com o tier.
    ///
    /// E o gancho que as transformacoes vao usar -- SSJ dourado, Kaioken vermelho, Blue azul.
    /// Enquanto elas nao existem, ninguem chama isto e ninguem brilha, que e o certo.
    /// </summary>
    /// <summary>
    /// A FOLHA DESTA AURA. Toda forma usa a base; so as linhas Legendary trocam. Quem DECIDE e o
    /// Core (`Catalogo.Folha`) e quem TRADUZ o simbolo em caminho e a
    /// <see cref="SpriteDeAura.CaminhoDa"/> -- o `switch` que estava escrito aqui era a segunda de
    /// tres copias da mesma tabela.
    ///
    /// ============================ E HA UM SIMBOLO QUE APAGA ESTE DESENHO ============================
    /// `FolhaDeAura.Nebulosa` quer dizer "esta forma nao usa folha" (o Ultra Instinto e o `ultra_ego`
    /// desenham a nuvem -- a mesma, em duas paletas).
    /// O `SpriteDeAura` obedece sozinho -- nao ha `if` a acrescentar aqui, e e de proposito: os DOIS
    /// desenhistas recebem o mesmo simbolo, entao os dois ficam mudos no mesmo instante e nao ha estado
    /// em que um saiba e o outro nao (foi assim que a chama da carga nasceu certa e a da forma errada).
    ///
    /// A `PointLight2D` NAO entra nisso: quem desenha muda, quem ILUMINA nao. Um Ultra Instinto
    /// carregando no escuro continua acendendo o cenario na cor da forma -- a luz responde a `HaLuz`,
    /// que pergunta se ha CHAMA e nao se ha folha.
    /// ==========================================================================================
    /// </summary>
    public void Folha(Jandirus.Core.Forms.FolhaDeAura f) => _desenho.DefinirFolha(f);

    /// <summary>
    /// ============================ A FORMA PREPARA, MAS NAO ACENDE ============================
    /// O dono: "a aura ja ta vindo ativada nas transformaçoes, e pra ela vir desativada e so
    /// ativar se o ki passar de 100% ou eu apertar C".
    ///
    /// Faz sentido e muda o que a aura SIGNIFICA: ela deixa de dizer "estou transformado" e passa
    /// a dizer "estou reunindo energia AGORA". Transformar sozinho nao acende nada.
    ///
    /// A forma continua mandando a COR e a FOLHA -- elas ficam guardadas aqui esperando. Quem
    /// puxa o gatilho e a carga (a tecla C) ou a sobrecarga (Ki acima do teto), pelo canal de
    /// efeito do servidor. Guardar em vez de acender e o que permite a aura sair na cor certa no
    /// instante em que ela FOR acesa, sem a forma precisar avisar de novo.
    /// ================================================================================
    /// </summary>
    /// <summary>Se a aura esta acesa agora. Pra bancada.</summary>
    public bool AcesaDeTeste => _acesa;

    /// <summary>
    /// ============================ SO UMA CHAMA POR CORPO, E A LUZ E DELA ============================
    /// A `CargaVisual` desenha a MESMA arte com o proprio `SpriteDeAura`. Se as duas aparecerem,
    /// o jogador ve duas chamas empilhadas -- foi o que o dono fotografou: uma grande clara atras
    /// e a dourada na frente. Entao a carga avisa aqui e a aura de forma cede o desenho.
    ///
    /// ISTO NAO E MAIS SO UMA SUPRESSAO, e essa era a raiz do defeito seguinte ("a aura das
    /// transformaçoes n estao brilhando ao apertar C"). O metodo antigo (`SuprimirDesenho`) so
    /// escondia o desenho e deixava um comentario dizendo que "a luz continua, porque ela responde
    /// a forma e nao a carga" -- regra do mundo ANTIGO, em que transformar acendia. Depois que a
    /// forma passou a so PREPARAR (ver o bloco abaixo), ninguem mais chamava `Acender` no jogo:
    /// quem desenhava a chama era a carga, e a carga nao tinha luz nenhuma. A chama saia cinza no
    /// escuro porque so havia sprite.
    ///
    /// Agora a carga entrega a FORCA, e a luz sai da mesma verdade que o desenho -- desde que haja
    /// FORMA. "Se ha chama, ha luz" sem essa ressalva foi o defeito seguinte: na base a chama da
    /// carga acendia a aura, e a aura na base nao existe (ver a guarda em <see cref="Aplicar"/>).
    /// E continua havendo um dono so da `PointLight2D` -- este node --, em vez de a `CargaVisual`
    /// ganhar uma segunda e as duas somarem energia no mesmo corpo.
    /// ==========================================================================================
    ///
    /// ============================ E A COR NAO VEM MAIS JUNTO ============================
    /// Ela vinha, e era o defeito seguinte: o dono, na base, "quando o ki passa de 100% ele fica
    /// brilhando de outra cor". A `CargaVisual` tinha DUAS cores proprias (um azul de carga e um
    /// laranja de sobrecarga) e as mandava pra ca -- ou seja havia tres respostas pra "de que cor e
    /// a chama deste corpo", e a que ganhava era a de quem escreveu por ultimo.
    ///
    /// So a FORCA e da carga (ela pulsa, e a sobrecarga pulsa mais forte e mais rapido). A COR e
    /// sempre <see cref="CorDaChama"/>, que a forma guardou -- e na base essa cor e o
    /// <see cref="CorDoKiCru"/>, exatamente "a mesma de sempre" que o dono esperava ver.
    /// ==================================================================================
    /// </summary>
    public void ChamaDaCarga(bool ativa, float forca)
    {
        _cargaAtiva = ativa;
        _forcaCarga = forca;
        Aplicar();
    }

    private bool _cargaAtiva;
    private float _forcaCarga;

    /// <param name="temForma">
    /// ESTE CORPO ESTA VESTINDO UMA FORMA? E o unico jeito honesto de este node saber -- quem
    /// veste sabe (`def == null` no <c>World.PrepararAuraDaForma</c>, `ehBase` no
    /// <c>Transformacao.Vestir</c>) e este node nao tem de onde deduzir. Deduzir pela COR nao
    /// serve: a base e o `CorDoKiCru` e a cinematica do Oozaru acende essa MESMA cor de proposito;
    /// deduzir pela FOLHA muito menos, porque quase toda forma usa a folha `Base`.
    /// Ver a guarda em <see cref="Aplicar"/>: e ele que decide se a luz existe.
    /// </param>
    /// <param name="d">
    /// A FORMA VESTIDA, e nao a cor dela ja resolvida. Era `(Color cor, float forca)` -- as duas
    /// contas escritas nos dois chamadores --, e passar a forma inteira e o que permite este node
    /// saber DE QUEM era a cor que ele guardou (<see cref="_chamaEhPessoal"/>). Com a cor pronta na
    /// mao ele so poderia deduzir isso comparando valores, que e a familia de erro que o
    /// `<paramref name="temForma"/>` logo abaixo existe pra evitar.
    /// </param>
    public void Preparar(Jandirus.Core.Forms.FormaDef? d, bool temForma)
    {
        // A COR E A FORCA SAO PERGUNTADAS AQUI, uma vez, e nao em cada chamador. Elas eram duas
        // linhas repetidas no `World.PrepararAuraDaForma` e no `Transformacao.Vestir`; com a cor
        // pessoal entrando na conta, seriam duas linhas repetidas que ainda precisariam achar o
        // corpo certo -- e este node E o corpo certo.
        _chamaEhPessoal = Jandirus.Core.Forms.Catalogo.ChamaDoJogador(d);
        _corAcesa = CorDaChamaDe(d, _corPessoal);
        _forcaAcesa = ForcaDaChamaDe(d);
        _temForma = temForma;
        // PREPARAR NAO ACENDE -- mas repinta o que JA estiver aceso. Vale pros dois donos da chama:
        // se a forma trocar com o C segurado, o desenho e a luz da carga trocam de cor no mesmo
        // quadro, porque a cor que os dois usam e esta que acabou de ser escrita.
        //
        // E E POR AQUI QUE A LUZ NASCE E MORRE NA TROCA DE FORMA com o C segurado: transformar
        // com `_cargaAtiva` acende (passou a haver forma) e voltar pra base apaga, no mesmo quadro.
        if (_acesa || _cargaAtiva) Aplicar();
    }

    /// <summary>
    /// Ver <see cref="Preparar"/>. Padrao FALSO porque um corpo que ainda nao recebeu forma
    /// nenhuma ESTA na base -- e na base este node nao acende (ver <see cref="Aplicar"/>).
    /// </summary>
    private bool _temForma;

    /// <summary>
    /// ACENDE COM ESTA COR, agora. E o emprestimo: em jogo so a cinematica do Oozaru o usa
    /// (`Transformacao.AcenderAuraBase`), e ela pede a <see cref="CorPessoal"/> deste corpo.
    ///
    /// A COR VEM DE FORA, ENTAO ELA NAO E "A DO JOGADOR" -- <see cref="_chamaEhPessoal"/> cai. E a
    /// leitura honesta: quem chamou aqui escolheu um valor, e uma ficha que chegasse no meio do
    /// emprestimo nao pode repintar uma chama que a cena esta usando. Quem devolve o node ao regime
    /// normal e o `Preparar` seguinte (o `Assumir` da cena faz isso antes de vestir a forma).
    /// </summary>
    public void Acender(Color cor, float forca = 1f)
    {
        if (forca <= 0) { Apagar(); return; }

        _acesa = true; _corAcesa = cor; _forcaAcesa = forca; _chamaEhPessoal = false;
        Aplicar();
    }

    /// <summary>
    /// HA LUZ NESTE CORPO AGORA? Uma expressao so, lida pelo <see cref="Aplicar"/> (que a obedece) e
    /// pelo <see cref="_Process"/> (que so trabalha se ela for verdadeira). Escrever a condicao nos
    /// dois lugares e como as regras da luz e do desenho ja divergiram antes.
    /// </summary>
    private bool HaLuz => _acesa || (_cargaAtiva && _temForma);

    /// <summary>
    /// O DESENHO E A LUZ SAEM DAQUI, os dois, do mesmo estado -- e e so por isso que eles nao
    /// divergem de novo. Cada vez que uma regra foi ligada num e esquecida no outro virou defeito.
    /// </summary>
    private void Aplicar()
    {
        // QUEM DESENHA A CHAMA AGORA: a carga tem a vez. Uma chama por corpo (ver `ChamaDaCarga`).
        // A COR VAI SEMPRE; quem decide se ela e USADA e o shader (uniform `tingir`, ligado a
        // `SpriteDeAura.SemTinta`). A versao anterior mandava BRANCO pra folha ja colorida, e o
        // resultado foi a aura ficar branca -- branco multiplicando a intensidade APAGA a arte.
        _desenho.Definir(_acesa && !_cargaAtiva, _corAcesa,
                         Mathf.Clamp(0.6f + _forcaAcesa * 0.22f, 0.4f, 1.4f));

        // ============================ NA BASE ESTE NODE NAO ENTRA EM JOGO ============================
        // Palavras do dono, depois de ver a foto: "ao passar de 100% do ki na base o node aura liga a
        // luz, sendo q na base n importa a % do ki, a unica coisa q deve ficar ativa e o node carga".
        //
        // A linha que estava aqui era `if (!_acesa && !_cargaAtiva)` -- ou seja "se ha chama, ha luz",
        // sem perguntar de QUEM e a chama. Ela consertou o defeito oposto (a chama do C sem brilho
        // nenhum no escuro), mas a chama da carga tambem conta na BASE: dai a luz virou funcao da % de
        // Ki de quem nao tem forma nenhuma, que e exatamente o que o dono viu.
        //
        // A guarda e `_temForma` e ela e EXPLICITA, escrita por quem veste a forma (ver `Preparar`) --
        // nao um efeito colateral da cor, da folha ou da forca, que sao os tres jeitos de isso voltar a
        // acender sozinho quando alguem mexer num degrau novo.
        //
        // `_acesa` PASSA sem a guarda, e isso e deliberado e nao brecha: `Acender` e alguem PEDINDO
        // esta aura agora -- em jogo, so a cinematica do Oozaru (`Transformacao.AcenderAuraBase`, o
        // `Efeito.AuraBase` que o proprio dono pediu, e que ele mesmo manda apagar no `Assumir`). Isso
        // nao e "estar na base com o Ki alto"; e uma cena que tomou o node emprestado e o devolve.
        //
        // A LUZ NAO MUDA DE DONO. A `CargaVisual` continua sem `PointLight2D`, e por isso a base fica
        // literalmente SEM luz -- que e o que a frase do dono diz: o que fica ativo na base e o node
        // carga, e o node carga nunca iluminou nada. Dar uma luz a ele seria um TERCEIRO dono do mesmo
        // efeito (ja somos dois desenhando a mesma arte) e ainda por cima acenderia na base de novo,
        // que e a queixa que este conserto existe pra matar.
        // ==========================================================================================
        if (!HaLuz) { _luz.Enabled = false; _luz.Energy = 0; return; }

        // SO A FORCA MUDA DE DONO; a cor nao tem de quem mudar. Havia aqui um `CorDaLuz(pedida)` que
        // escolhia entre a cor da carga e a da forma conforme a folha se tingisse ou nao -- remendo
        // que so existia porque a carga tinha cor propria. Sem ela, a chama desenhada e a luz saem
        // do mesmo `_corAcesa` por construcao, e nao ha mais como uma divergir da outra.
        float forca = _cargaAtiva ? _forcaCarga : _forcaAcesa;
        _luz.Color = _corAcesa;

        // ============================ A LUZ E DA NOITE ============================
        // Pedido do dono: "que a aura de transformacao so brilhe ao anoitecer, porque de dia fica
        // muito saturado e muito claro". E ele esta certo pela fisica da cena: o `CanvasModulate`
        // do meio-dia ja deixa tudo perto do branco, entao somar uma PointLight2D por cima nao
        // acrescenta brilho -- estoura, e o personagem some dentro do proprio efeito.
        //
        // O QUE SOME E SO A LUZ. O desenho da aura (os fachos) e o contorno no sprite continuam
        // valendo de dia: eles sao como se SABE que alguem esta transformado, e apagar isso ao
        // meio-dia tiraria a leitura da transformacao justamente no horario mais jogado.
        // ========================================================================
        // ============================ CURVA, NAO RAMPA ============================
        // A primeira versao usava a escuridao crua. Duas consequencias, as duas vistas em jogo:
        //
        //  * A luz brilhava FRAQUINHO o dia inteiro (escuridao ~0,15 ao meio-dia ja passava do
        //    limiar) -- e "so brilha a noite" com um fiapo aceso o dia todo nao e "so a noite".
        //  * ANTES DE O SERVIDOR MANDAR A HORA a `Iluminacao` usa `AmbienteDia`, que tambem da
        //    ~0,15. Entao ao transformar de dia a luz acendia por alguns quadros e apagava quando a
        //    hora real chegava -- o clarao que o dono viu.
        //
        // Com o `SmoothStep`, abaixo de 0,35 de escuridao a luz e ZERO, ponto. Ela so comeca a
        // existir no entardecer e chega inteira no breu.
        // =======================================================================
        float noite = (float)Mathf.SmoothStep(0.35, 0.8, Iluminacao.Escuridao);

        // ============================ A LUZ LAVAVA A CENA ============================
        // `Energy = forca` cru chegava a 2,3 numa forma alta, e o resultado nas fotos era um
        // borrao claro de uns 400 px: o chao estourado, o proprio personagem sem contraste, e os
        // efeitos novos (raio e contorno) invisiveis DENTRO da luz que deveria acompanha-los.
        //
        // A raiz e que `forca` cresce com o degrau da forma e ia direto pro `Energy`. Agora ela
        // entra com um teto bem mais baixo: uma forma mais alta continua acendendo MAIS, so que a
        // diferenca se mede em alcance e nao em estouro.
        // ==========================================================================
        _luz.Energy = Mathf.Clamp(0.35f + forca * 0.28f, 0.1f, 1.3f) * noite;
        _luz.TextureScale = Mathf.Clamp(0.6f + forca * 0.22f, 0.5f, 1.6f);

        // ZERO E ZERO. Uma PointLight2D com energia 0,02 nao aparece e mesmo assim custa uma
        // passada de luz por quadro, por corpo.
        _luz.Enabled = noite > 0.01f;
    }

    /// <summary>
    /// A LUZ ACOMPANHA O ENTARDECER. Sem isto, quem acendeu ao meio-dia continuaria sem luz a
    /// noite inteira -- a energia so seria reescrita na PROXIMA troca de forma, que pode nao vir.
    ///
    /// Recalcular todo quadro seria desperdicio: a escuridao muda devagar. Uma vez por segundo
    /// basta e o olho nao ve o degrau. (Enquanto o C esta segurado a `CargaVisual` ja repinta todo
    /// quadro, porque a chama dela pulsa -- este relogio e pra quem esta aceso e parado.)
    /// </summary>
    public override void _Process(double delta)
    {
        if (!_luz.Enabled && Iluminacao.Escuridao <= 0.05f) { _relogio = 0; return; }

        _relogio += delta;
        if (_relogio < 1) return;
        _relogio = 0;
        // A MESMA CONDICAO DO `Aplicar` (ver <see cref="HaLuz"/>): sem ela, um corpo na base segurando
        // C acordaria este relogio toda noite pra recalcular uma luz que a guarda ja recusou.
        if (HaLuz) Aplicar();
    }

    private double _relogio;
    private bool _acesa;

    /// <summary>Ver <see cref="CorDoKiCru"/>: o padrao e a base, porque um corpo sem forma esta na base.</summary>
    private Color _corAcesa = CorDoKiCru;
    private float _forcaAcesa;

    /// <summary>Energia da luz agora. Pra bancada -- ver `--diagforma`.</summary>
    public float EnergiaDeTeste => _luz.Enabled ? _luz.Energy : 0;

    /// <summary>A cor que a luz esta lancando agora. Pra bancada.</summary>
    public Color CorDaLuzDeTeste => _luz.Color;

    /// <summary>
    /// ============================ DE QUE COR E A CHAMA DESTE CORPO ============================
    /// UMA resposta so, e ela mora aqui. A forma escreve por `Preparar`; quem DESENHA a chama le
    /// daqui -- este node quando a aura de forma esta acesa, e a <see cref="CargaVisual"/> quando o
    /// jogador segura C ou passa dos 100% de Ki. A luz da <see cref="_luz"/> usa a mesma.
    ///
    /// Era `CorGuardadaDeTeste`, so pra bancada, enquanto a carga tinha as duas cores dela. Virar
    /// API de verdade e o conserto: um segundo lugar guardando "a cor da chama" e como nasceu o
    /// laranja fixo que o dono viu na base ao passar dos 100%.
    ///
    /// GUARDADA E NAO ACESA: `Apagar()` nao a desfaz -- ele desliga a luz e o desenho e deixa isto
    /// como esta. Era ai que morava a segunda queixa do dono ("a aura da base ainda ta brilhando, e
    /// ela sai DOURADA"): voltar pra base apagava, mas o dourado do Super Saiyajin continuava
    /// guardado esperando a proxima tecla C. Medir `AcesaDeTeste` nao ve isso -- ele diz que esta
    /// apagada, e esta. O defeito e o que ela vai fazer DEPOIS. Quem desfaz e o ramo `def == null`
    /// do `World.PrepararAuraDaForma`, que reescreve cor E folha na volta.
    /// ========================================================================================
    /// </summary>
    public Color CorDaChama => _corAcesa;

    /// <summary>
    /// Apaga a aura DA FORMA. Nao apaga a luz na marra: se o C ainda estiver segurado, a chama da
    /// carga continua desenhada e continua iluminando -- quem manda na luz e "ha chama", e nao "ha
    /// forma". Por isso passa pelo `Aplicar` em vez de zerar aqui.
    /// </summary>
    public void Apagar()
    {
        _acesa = false;
        Aplicar();
    }

    /// <summary>
    /// Textura de luz radial gerada em codigo: nao depende de arte importada.
    ///
    /// PUBLICA porque a <see cref="CargaVisual"/> quer a MESMA textura. Duas receitas de degrade
    /// dariam duas luzes com queda de borda diferente no mesmo corpo -- e a de carga e justamente
    /// a que acende ao lado da de forma.
    /// </summary>
    public static GradientTexture2D Radial(int raio)
    {
        var g = new Gradient();
        g.SetColor(0, Colors.White);
        g.SetColor(1, new Color(1, 1, 1, 0));
        return new GradientTexture2D
        {
            Gradient = g,
            Width = raio * 2,
            Height = raio * 2,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };
    }
}
