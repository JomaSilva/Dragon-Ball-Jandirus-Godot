namespace Jandirus.Core.World;

/// <summary>As 4 direcoes de sprite do BYOND. A arte convertida so tem estas (e as 8 nos blasts).</summary>
public enum Facing : byte { South = 0, North = 1, East = 2, West = 3 }

/// <summary>
/// REGRA DE MOVIMENTO: MORA AQUI, E SO AQUI.
///
/// O cliente chama <see cref="Integrate"/> pra andar (movimento livre em pixel, resposta
/// imediata) e o servidor chama <see cref="ValidateStep"/> pra conferir o que chegou. Como
/// as duas pontas usam ESTE arquivo, "cliente calcula, servidor valida" nao vira duas
/// implementacoes que divergem com o tempo.
///
/// O servidor NAO exige o mesmo float bit a bit: ele checa se o deslocamento cabe no que o
/// tempo decorrido permite, com uma folga pra jitter de rede. Exigir igualdade exata com
/// float e uma armadilha classica (o mesmo codigo da resultados diferentes entre maquinas).
/// </summary>
public static class MoveRules
{
    // ============================== CONFIG ======================================
    public const float BaseSpeedPx = 160f;     // pixels por segundo em stats base (5 tiles/s a 32px)
    public const float MaxDeltaSeconds = 0.25f;// teto do dt aceito num pacote: sem isso, "passei 10s offline" = teleporte
    public const float SpeedTolerance = 1.35f; // folga de validacao (jitter/aceleracao); acima disso e correcao
    public const float MinCorrectionPx = 6f;   // erro abaixo disto nao vale corrigir (evita briga cliente-servidor)

    /// <summary>
    /// A CAIXA DOS PES. O personagem tem 32px de altura mas so a base dele ocupa chao -- a
    /// cabeca passa por cima do muro no desenho, como no BYOND. O centro do sprite fica no
    /// meio do corpo, entao a caixa desce <see cref="FeetOffsetY"/> pra encostar no chao.
    /// </summary>
    public const float BodyHalfW = 8f;
    public const float BodyHalfH = 5f;
    public const float FeetOffsetY = 8f;
    // ============================ FIM DO CONFIG =================================

    /// <summary>Velocidade final em px/s. O stat vem do Core de stats; 1.0 = base.</summary>
    public static float SpeedPx(float speedStat) => BaseSpeedPx * (speedStat <= 0 ? 1f : speedStat);

    /// <summary>
    /// Converte o Espeed (o stat de velocidade DEPOIS da curva de retorno decrescente) no
    /// multiplicador de movimento. A ancora e 2: um personagem de speed cru 1 sai do Statify
    /// com Espeed 2 (1 de base + 1 do buff temporario neutro), e esse e o andar "normal".
    /// Como o Espeed satura perto de 10, o teto de velocidade fica em ~5x -- ninguem some da
    /// tela por ter treinado velocidade.
    /// </summary>
    public const float EspeedBase = 2f;
    public static float SpeedStatFrom(double espeed) => (float)Math.Max(espeed / EspeedBase, 0.1);

    /// <summary>Passo do movimento livre, IGNORANDO parede. <paramref name="dir"/> nao precisa vir normalizado.</summary>
    public static Vec2 Integrate(Vec2 pos, Vec2 dir, float dtSeconds, float speedStat)
    {
        if (dtSeconds <= 0) return pos;
        if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;
        Vec2 step = dir.Normalized() * (SpeedPx(speedStat) * dtSeconds);
        return pos + step;
    }

    /// <summary>
    /// O PASSO DE VERDADE: anda respeitando parede, com deslize nas quinas.
    ///
    /// O cliente chama isto pra se mover e o servidor chama <see cref="ValidateStep"/>, que
    /// usa a MESMA <see cref="Occupied"/>. E o que elimina a briga entre os dois: antes o
    /// cliente atravessava a parede (o TileMap convertido nao tem fisica), o servidor recusava
    /// e devolvia correcao, o cliente empurrava de novo -- e o personagem TREMIA na parede.
    ///
    /// Deslize: se o passo cheio nao cabe, tenta so o eixo X e depois so o Y. E o que faz
    /// andar rente a um muro em diagonal continuar andando em vez de travar.
    /// </summary>
    public static Vec2 Advance(Vec2 pos, Vec2 dir, float dtSeconds, float speedStat,
                               ZoneCollision? mapa, out bool blocked)
    {
        blocked = false;
        if (dtSeconds <= 0) return pos;
        if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;

        Vec2 step = dir.Normalized() * (SpeedPx(speedStat) * dtSeconds);
        if (step.LengthSquared < 1e-9f) return pos;

        Vec2 alvo = pos + step;
        if (mapa == null) return alvo;

        // ja preso dentro de parede (spawn ruim, mapa recarregado): deixa sair
        if (Occupied(mapa, pos)) return alvo;
        if (!Occupied(mapa, alvo)) return alvo;

        blocked = true;
        if (step.X != 0)
        {
            var sx = new Vec2(alvo.X, pos.Y);
            if (!Occupied(mapa, sx)) return sx;
        }
        if (step.Y != 0)
        {
            var sy = new Vec2(pos.X, alvo.Y);
            if (!Occupied(mapa, sy)) return sy;
        }
        return pos;   // encostou de frente: para
    }

    /// <summary>
    /// O caminho de <paramref name="from"/> ate <paramref name="to"/> encosta em parede?
    ///
    /// Usa a MESMA caixa dos pes do <see cref="Advance"/>. O `PathBlocked` do mapa testa um
    /// PONTO no centro do corpo, 3 px acima do topo da caixa -- e essa diferenca de 3 px era
    /// uma faixa onde o servidor reprovava um passo que o cliente considerou legal, gerando
    /// correcao em jogo honesto. Regra compartilhada so vale se for a MESMA regra.
    /// </summary>
    public static bool PathOccupied(ZoneCollision mapa, Vec2 from, Vec2 to)
    {
        Vec2 d = to - from;
        float dist = d.Length;
        int passos = Math.Max(1, (int)MathF.Ceiling(dist / (ZoneCollision.TileSize * 0.5f)));
        for (int i = 1; i <= passos; i++)
            if (Occupied(mapa, from + d * (i / (float)passos))) return true;
        return false;
    }

    /// <summary>A caixa dos pes encosta em parede nesta posicao?</summary>
    public static bool Occupied(ZoneCollision mapa, Vec2 centro)
    {
        float y = centro.Y + FeetOffsetY;
        return mapa.BlockedAt(new Vec2(centro.X - BodyHalfW, y - BodyHalfH))
            || mapa.BlockedAt(new Vec2(centro.X + BodyHalfW, y - BodyHalfH))
            || mapa.BlockedAt(new Vec2(centro.X - BodyHalfW, y + BodyHalfH))
            || mapa.BlockedAt(new Vec2(centro.X + BodyHalfW, y + BodyHalfH));
    }

    /// <summary>
    /// O servidor confere o passo que o cliente afirma ter dado.
    /// Devolve true se aceitou; se recusou, <paramref name="corrected"/> traz a posicao
    /// mais longe que o cliente PODERIA ter alcancado na direcao que ele tentou.
    /// </summary>
    /// <summary>
    /// Validacao COMPLETA: velocidade + parede. O <paramref name="mapa"/> pode vir nulo
    /// (zona procedural ainda sem colisao carregada) e ai so a velocidade e conferida.
    /// </summary>
    public static bool ValidateStep(Vec2 from, Vec2 claimed, float dtSeconds, float speedStat,
        ZoneCollision? mapa, out Vec2 corrected)
    {
        if (!ValidateStep(from, claimed, dtSeconds, speedStat, out corrected)) return false;
        if (mapa == null) return true;

        // Ja estava dentro de parede? Nao ha o que conferir -- e mais importante deixar sair
        // do que insistir num veredito sobre uma posicao que ja era invalida.
        if (Occupied(mapa, from)) return true;

        // velocidade OK, mas atravessou parede? volta pra onde estava. A checagem e a MESMA
        // que o cliente usou pra andar -- divergir aqui gera correcao em jogo honesto, e
        // correcao em jogo honesto e o que o jogador ve como o personagem tremendo.
        if (PathOccupied(mapa, from, corrected))
        {
            corrected = from; // fica onde estava
            return false;
        }
        return true;
    }

    public static bool ValidateStep(Vec2 from, Vec2 claimed, float dtSeconds, float speedStat, out Vec2 corrected)
    {
        if (dtSeconds < 0) dtSeconds = 0;
        if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;

        float allowed = SpeedPx(speedStat) * dtSeconds * SpeedTolerance;
        Vec2 delta = claimed - from;
        float dist = delta.Length;

        if (dist <= allowed + MinCorrectionPx)
        {
            corrected = claimed;
            return true;
        }

        // andou demais: puxa de volta pro limite do que era possivel, mantendo a direcao
        corrected = from + delta.Normalized() * allowed;
        return false;
    }

    /// <summary>
    /// Direcao do sprite a partir do vetor de movimento. O eixo DOMINANTE vence, e o
    /// desempate favorece o horizontal (mesma sensacao do BYOND, que nunca teve diagonal
    /// de sprite pra personagem).
    /// </summary>
    public static Facing FacingFrom(Vec2 dir, Facing atual)
    {
        if (dir.LengthSquared < 1e-6f) return atual; // parado mantem pra onde olhava
        return MathF.Abs(dir.X) >= MathF.Abs(dir.Y)
            ? (dir.X >= 0 ? Facing.East : Facing.West)
            : (dir.Y >= 0 ? Facing.South : Facing.North);
    }

    /// <summary>Sufixo que o conversor de .dmi gravou no nome da animacao.</summary>
    public static string FacingSuffix(Facing f) => f switch
    {
        Facing.North => "north",
        Facing.East => "east",
        Facing.West => "west",
        _ => "south",
    };
}
