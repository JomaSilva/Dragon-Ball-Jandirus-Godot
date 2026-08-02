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
    }

    /// <summary>
    /// As zonas do corpo que da pra mirar. A zona nao GARANTE o membro -- so pesa o sorteio a
    /// favor dele (ver <see cref="Jandirus.Core.Combat.Body.Sortear"/>).
    /// </summary>
    public static readonly string[] Zonas = ["", "cabeca", "torso", "bracos", "pernas"];

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

        public void Write(NetDataWriter w)
        {
            w.Put(Atacante); w.Put(Alvo); w.Put(Desfecho); w.Put(Nivel);
            w.Put(TemDano);
            if (TemDano) { w.Put(Dano); w.Put(Membro ?? ""); }
            w.Put((byte)((Quebrou ? 1 : 0) | (Decepou ? 2 : 0) | (Nocauteou ? 4 : 0)
                       | (Morreu ? 8 : 0) | (Rabo ? 16 : 0)));
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

    /// <summary>Nem anda nem golpeia: caido ou morto.</summary>
    public bool Imobilizado => KO || Morto;

    public void Write(NetDataWriter w)
    {
        w.Put(Class); w.Put(BP); w.Put(ExpressedBP);
        w.Put(Ki); w.Put(MaxKi); w.Put(HP); w.Put(SpeedStat);
        w.Put(SocoMs); w.Put(MembrosRuins); w.Put(Estado);
    }

    public static SheetState Read(NetDataReader r) => new()
    {
        Class = r.GetString(32), BP = r.GetDouble(), ExpressedBP = r.GetDouble(),
        Ki = r.GetDouble(), MaxKi = r.GetDouble(), HP = r.GetDouble(), SpeedStat = r.GetFloat(),
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

    public void Write(NetDataWriter w)
    {
        w.Put(Id);
        w.PutVec(Pos);
        // direcao (2 bits) + pose (3 bits) + "andando" (1 bit) no MESMO byte
        w.Put((byte)((Facing & 0x03) | ((byte)Pose & 0x07) << 2 | (Moving ? 0x80 : 0x00)));
        w.Put(Vida);
    }

    public static EntityState Read(NetDataReader r)
    {
        var e = new EntityState { Id = r.GetInt(), Pos = r.GetVec() };
        byte flags = r.GetByte();
        e.Facing = (byte)(flags & 0x03);
        e.Pose = (Protocol.Pose)((flags >> 2) & 0x07);
        e.Moving = (flags & 0x80) != 0;
        e.Vida = r.GetByte();
        return e;
    }
}
