namespace Jandirus.Core.World;

/// <summary>
/// A geometria de uma zona, do jeito que o SERVIDOR consegue usar: 1 bit por celula.
///
/// O cliente colide pelo TileMap do Godot (fisica local, resposta imediata). O servidor nao
/// pode instanciar uma cena de 250 mil tiles pra cada planeta -- entao le este mesmo dado
/// numa forma compacta (um andar de 500x500 = ~31 KB) e faz a conferencia por conta propria.
///
/// Como o Core nao conhece o Godot, quem carrega os BYTES e cada ponta do seu jeito
/// (File.ReadAllBytes no servidor, FileAccess do res:// no cliente).
/// </summary>
public sealed class ZoneCollision
{
    public const int TileSize = 32;

    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _bits;

    private ZoneCollision(int w, int h, byte[] bits)
    {
        Width = w; Height = h; _bits = bits;
    }

    /// <summary>Cabecalho "JCOL" + uint16 largura + uint16 altura + bitset em ordem de linha.</summary>
    public static ZoneCollision? Load(byte[] data)
    {
        if (data.Length < 8 || data[0] != 'J' || data[1] != 'C' || data[2] != 'O' || data[3] != 'L')
            return null;
        int w = data[4] | (data[5] << 8);
        int h = data[6] | (data[7] << 8);
        int precisa = (w * h + 7) / 8;
        if (w <= 0 || h <= 0 || data.Length < 8 + precisa) return null;

        var bits = new byte[precisa];
        Array.Copy(data, 8, bits, 0, precisa);
        return new ZoneCollision(w, h, bits);
    }

    public bool BlockedCell(int cx, int cy)
    {
        // fora do mapa conta como parede: ninguem sai pela borda
        if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return true;
        int i = cy * Width + cx;
        return (_bits[i >> 3] & (1 << (i & 7))) != 0;
    }

    public bool BlockedAt(Vec2 pos) =>
        BlockedCell((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize));

    /// <summary>
    /// O caminho de <paramref name="from"/> ate <paramref name="to"/> passa por parede?
    ///
    /// Amostra o segmento a cada meio tile. Nao e um raycast exato de proposito: o objetivo
    /// e pegar quem ATRAVESSA parede, nao disputar o pixel com a fisica do cliente -- e uma
    /// checagem cara demais rodaria 30x por segundo por jogador.
    /// </summary>
    public bool PathBlocked(Vec2 from, Vec2 to)
    {
        Vec2 d = to - from;
        float dist = d.Length;
        if (dist < 0.01f) return BlockedAt(to);

        int passos = (int)MathF.Ceiling(dist / (TileSize * 0.5f));
        for (int i = 1; i <= passos; i++)
        {
            Vec2 p = from + d * (i / (float)passos);
            if (BlockedAt(p)) return true;
        }
        return false;
    }
}
