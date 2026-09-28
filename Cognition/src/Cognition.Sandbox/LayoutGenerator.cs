using System.Text;
using Cognition.Core.Config;

namespace Cognition.Sandbox;

/// <summary>
/// Random station-like layouts (rooms split by walls with doors, scattered pillars and tables) for property
/// tests and load runs. Same seed, same world.
/// </summary>
public static class LayoutGenerator
{
    public static string Map(int seed, int width = 40, int height = 30)
    {
        var random = new Random(seed);
        var cells = new char[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[y, x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : '.';
            }
        }

        Split(cells, random, 1, 1, width - 2, height - 2, depth: 0);

        for (var i = 0; i < width * height / 40; i++)
        {
            var x = random.Next(1, width - 1);
            var y = random.Next(1, height - 1);
            if (cells[y, x] == '.')
                cells[y, x] = random.Next(3) == 0 ? 'T' : '#';
        }

        var sb = new StringBuilder();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                sb.Append(cells[y, x]);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void Split(char[,] cells, Random random, int x0, int y0, int x1, int y1, int depth)
    {
        var w = x1 - x0 + 1;
        var h = y1 - y0 + 1;
        if (depth > 4 || (w < 9 && h < 9))
            return;
        var vertical = w >= h;
        if (vertical)
        {
            var x = random.Next(x0 + 3, x1 - 2);
            for (var y = y0; y <= y1; y++)
            {
                cells[y, x] = '#';
            }

            cells[random.Next(y0, y1 + 1), x] = Door(random);
            Split(cells, random, x0, y0, x - 1, y1, depth + 1);
            Split(cells, random, x + 1, y0, x1, y1, depth + 1);
        }
        else
        {
            var y = random.Next(y0 + 3, y1 - 2);
            for (var x = x0; x <= x1; x++)
            {
                cells[y, x] = '#';
            }

            cells[y, random.Next(x0, x1 + 1)] = Door(random);
            Split(cells, random, x0, y0, x1, y - 1, depth + 1);
            Split(cells, random, x0, y + 1, x1, y1, depth + 1);
        }
    }

    private static char Door(Random random) => random.Next(3) switch
    {
        0 => 'D',
        1 => 'O',
        _ => 'L',
    };

    /// <summary>A generated world with <paramref name="agents"/> agents and <paramref name="items"/> items on random floor cells.</summary>
    public static SandboxWorld World(int seed, CognitionConfig config, int agents = 10, int items = 30,
        SandboxOptions? options = null, int width = 40, int height = 30)
    {
        var world = SandboxWorld.FromMap(Map(seed, width, height), config, (options ?? new SandboxOptions()) with { Seed = seed });
        var random = new Random(seed ^ 0x5eed);
        var floor = world.Grid.Cells().Where(world.Grid.IsWalkable).ToList();
        for (var i = 0; i < agents; i++)
        {
            var cell = floor[random.Next(floor.Count)];
            var guid = new Guid(i + 1, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, (byte)(seed & 0xff));
            world.AddAgent($"agent{i + 1}", $"Agent {i + 1}", guid, $"person number {i + 1}", cell);
        }

        var kinds = Enum.GetValues<ItemKind>();
        var spots = world.Grid.Cells().Where(c => world.Grid[c] != Tile.Wall).ToList();
        for (var i = 0; i < items; i++)
        {
            var kind = kinds[random.Next(kinds.Length)];
            world.AddItem(kind, kind.ToString().ToLowerInvariant(), spots[random.Next(spots.Count)]);
        }

        return world;
    }
}
