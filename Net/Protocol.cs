using Jandirus.Core.Appearance;
using Jandirus.Core.Races;
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
    }

    /// <summary>
    /// OS CANAIS DE FALA, com os MESMOS NUMEROS do `sayType()` do BYOND.
    ///
    /// Nao e capricho: o original tem seis modos e cada um com um alcance proprio, e manter a
    /// numeracao deixa a comparacao com `Talking.dm` direta -- quem for conferir se o alcance
    /// do sussurro esta certo abre o `if(2)` de la e o `Sussurro` daqui.
    ///
    /// <see cref="Sistema"/> nao existe no original e NAO TRAFEGA: e o canal em que o proprio
    /// cliente escreve o que so interessa a quem esta jogando ("zona carregada", "sem Ki pro
    /// dash"). Fica fora da faixa 1-6 de proposito.
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
    /// </summary>
    public enum Pose : byte { Normal = 0, Treinando = 1, Meditando = 2, Atacando = 3, Voando = 4, Nocauteado = 5 }

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
        Skills = 15,       // o que eu aprendi e quantos marcos tenho
        Forma = 16,        // fulano mudou de forma: de, pra, e se foi a PRIMEIRA vez
        Cargos = 17,       // a lista de cargos: chave, quem ocupa, e o que falta PRA MIM
        Vizinhanca = 18,   // os planetas por perto no espaco (so quando a CHUNK muda)
        Efeito = 19,       // caiu um efeito em mim: id + por quantos ms (0 = passou)
        Construcoes = 20,  // as construcoes de pe na minha zona
        Tech = 21,         // meu nivel de tecnologia, meu zeni e o catalogo com o motivo de cada nao
        Estilos = 22,      // meu estilo ativo, os que aprendi e a maestria de cada um
        Zanzo = 23,        // fulano piscou: id + DE ONDE ele saiu (a miragem nasce la)
        Porta = 24,        // porta abriu ou fechou (ou: a lista inteira, ao entrar na zona)
        Cenario = 25,      // uma celula do cenario CAIU: virou chao (knockback contra parede)
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
        public bool Quebrou, Decepou, Nocauteou, Morreu, Rabo;

        /// <summary>
        /// O ATACANTE DEIXOU UMA IMAGEM REMANESCENTE ao investir (o Zanzoken).
        ///
        /// QUEM DECIDE E O SERVIDOR porque so ele sabe quais skills o OUTRO tem -- e o cliente nao
        /// deve saber: a lista de skills alheia e ficha, do mesmo naipe que o BP. Aqui so trafega
        /// o resultado ("houve vulto"), que e o que os olhos veriam de qualquer forma.
        ///
        /// CABE NUM BIT QUE JA SOBRAVA no byte de desfechos (o 32) -- ver o `Write` abaixo. Ainda
        /// ficam dois livres.
        /// </summary>
        public bool Zanzo;

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
            w.Put(TemDano);
            if (TemDano) { w.Put(Dano); w.Put(Membro ?? ""); }
            w.Put((byte)((Quebrou ? 1 : 0) | (Decepou ? 2 : 0) | (Nocauteou ? 4 : 0)
                       | (Morreu ? 8 : 0) | (Rabo ? 16 : 0) | (Zanzo ? 32 : 0) | (Investiu ? 64 : 0) | (ZanzoEsquiva ? 128 : 0)));
        }

        public static HitEvent Read(NetDataReader r)
        {
            var h = new HitEvent
            {
                Atacante = r.GetInt(), Alvo = r.GetInt(), Desfecho = r.GetByte(),
                Nivel = r.GetByte(), TemDano = r.GetBool(), Membro = "",
            };
            if (h.TemDano) { h.Dano = r.GetFloat(); h.Membro = r.GetString(32); }
            byte f = r.GetByte();
            h.Quebrou = (f & 1) != 0; h.Decepou = (f & 2) != 0;
            h.Nocauteou = (f & 4) != 0; h.Morreu = (f & 8) != 0; h.Rabo = (f & 16) != 0;
            h.Zanzo = (f & 32) != 0;
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
    }

    public static CharacterDraft GetDraft(this NetDataReader r) => new()
    {
        Name = r.GetString(24), Race = r.GetString(24), Planet = r.GetString(24),
        Gender = r.GetString(8), Age = r.GetInt(), ChosenClass = r.GetString(32),
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
        for (int i = 0; i < a.Roupa.Count && i < Appearance.MaxRoupa; i++) w.Put(a.Roupa[i]);
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
        for (int i = 0; i < n; i++) a.Roupa.Add(r.GetString(120));
        return a;
    }

    public static NetDataWriter Begin(C2S id) { var w = new NetDataWriter(); w.Put((byte)id); return w; }
    public static NetDataWriter Begin(S2C id) { var w = new NetDataWriter(); w.Put((byte)id); return w; }
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

    /// <summary>Nem anda nem golpeia: caido ou morto.</summary>
    public bool Imobilizado => KO || Morto;

    public void Write(NetDataWriter w)
    {
        w.Put(Class); w.Put(BP); w.Put(ExpressedBP);
        w.Put(Ki); w.Put(MaxKi); w.Put(HP); w.Put(Vigor); w.Put(VigorMax); w.Put(SpeedStat);
        w.Put(SocoMs); w.Put(MembrosRuins); w.Put(Estado);
    }

    public static SheetState Read(NetDataReader r) => new()
    {
        Class = r.GetString(32), BP = r.GetDouble(), ExpressedBP = r.GetDouble(),
        Ki = r.GetDouble(), MaxKi = r.GetDouble(), HP = r.GetDouble(),
        Vigor = r.GetDouble(), VigorMax = r.GetDouble(), SpeedStat = r.GetFloat(),
        SocoMs = r.GetInt(), MembrosRuins = r.GetByte(), Estado = r.GetByte(),
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

    /// <summary>
    /// Vida em porcento, num byte. Vai no snapshot porque um corpo machucado se VE -- e a
    /// unica coisa da ficha alheia que nao depende de scouter. Poder continua escondido.
    /// </summary>
    public byte Vida;

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

    private const byte BitCarregando = 0x01;
    private const byte BitSobrecarregado = 0x02;

    public void Write(NetDataWriter w)
    {
        w.Put(Id);
        w.PutVec(Pos);
        // direcao (2 bits) + pose (3 bits) + "andando" (1 bit) no MESMO byte
        w.Put((byte)((Facing & 0x03) | ((byte)Pose & 0x07) << 2
                   | (Rabo ? 0x20 : 0x00) | (Oculto ? 0x40 : 0x00)
                   | (Moving ? 0x80 : 0x00)));
        w.Put((byte)((Carregando ? BitCarregando : 0) | (Sobrecarregado ? BitSobrecarregado : 0)));
        w.Put(Vida);
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
        e.Vida = r.GetByte();
        return e;
    }
}
