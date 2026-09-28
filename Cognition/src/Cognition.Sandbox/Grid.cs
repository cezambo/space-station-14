namespace Cognition.Sandbox;

public enum Tile : byte
{
    Floor,
    Wall,
    Table,
}

public readonly record struct Cell(int X, int Y)
{
    public Cell Offset(int dx, int dy) => new(X + dx, Y + dy);
}

/// <summary>
/// Static tiles plus a per-cell opacity/blocking overlay that doors and furniture update. Line of sight uses a
/// supercover traversal: every cell whose interior the centre-to-centre segment crosses is checked, and a
/// segment passing exactly through a corner is blocked when both side cells are opaque (diagonal walls join,
/// as in SS14), so nothing leaks through diagonal gaps.
/// </summary>
public sealed class Grid
{
    private readonly Tile[] _tiles;
    private readonly bool[] _opaque;
    private readonly bool[] _blocked;

    public int Width { get; }
    public int Height { get; }

    public Grid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[width * height];
        _opaque = new bool[width * height];
        _blocked = new bool[width * height];
    }

    public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    private int I(Cell c) => c.Y * Width + c.X;

    public Tile this[Cell c]
    {
        get => InBounds(c) ? _tiles[I(c)] : Tile.Wall;
        set
        {
            _tiles[I(c)] = value;
            Refresh(c);
        }
    }

    /// <summary>Overlay from entities on the cell (closed doors, containers).</summary>
    internal void SetOverlay(Cell c, bool opaque, bool blocked)
    {
        _opaque[I(c)] = opaque || _tiles[I(c)] == Tile.Wall;
        _blocked[I(c)] = blocked || _tiles[I(c)] != Tile.Floor;
    }

    private void Refresh(Cell c) => SetOverlay(c, false, false);

    public bool IsOpaque(Cell c) => !InBounds(c) || _opaque[I(c)];

    public bool IsWalkable(Cell c) => InBounds(c) && !_blocked[I(c)];

    public IEnumerable<Cell> Cells()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                yield return new Cell(x, y);
            }
        }
    }

    /// <summary>Cells strictly between <paramref name="a"/> and <paramref name="b"/> touched by the centre segment.</summary>
    public IEnumerable<(Cell Cell, bool Corner, Cell Side1, Cell Side2)> Between(Cell a, Cell b)
    {
        int x = a.X, y = a.Y;
        int dx = b.X - a.X, dy = b.Y - a.Y;
        int nx = Math.Abs(dx), ny = Math.Abs(dy);
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        int ix = 0, iy = 0;
        while (ix < nx || iy < ny)
        {
            // Compare (0.5 + ix) / nx with (0.5 + iy) / ny exactly in integers.
            var decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
            if (decision == 0)
            {
                var side1 = new Cell(x + sx, y);
                var side2 = new Cell(x, y + sy);
                x += sx;
                y += sy;
                ix++;
                iy++;
                yield return (new Cell(x, y), true, side1, side2);
            }
            else if (decision < 0)
            {
                x += sx;
                ix++;
                yield return (new Cell(x, y), false, default, default);
            }
            else
            {
                y += sy;
                iy++;
                yield return (new Cell(x, y), false, default, default);
            }
        }
    }

    /// <summary>True when nothing opaque lies strictly between the two cells (the end cells themselves may be opaque).</summary>
    public bool LineOfSight(Cell a, Cell b)
    {
        foreach (var (cell, corner, s1, s2) in Between(a, b))
        {
            if (corner && IsOpaque(s1) && IsOpaque(s2))
                return false;
            if (cell != b && IsOpaque(cell))
                return false;
        }

        return true;
    }

    /// <summary>Distinct opaque cells crossed between the two cells (walls and closed doors), for sound (RP-02).</summary>
    public int WallsBetween(Cell a, Cell b)
    {
        var seen = new HashSet<Cell>();
        foreach (var (cell, corner, s1, s2) in Between(a, b))
        {
            if (cell != b && IsOpaque(cell))
                seen.Add(cell);
            else if (corner && IsOpaque(s1) && IsOpaque(s2))
                seen.Add(s1);
        }

        return seen.Count;
    }
}
