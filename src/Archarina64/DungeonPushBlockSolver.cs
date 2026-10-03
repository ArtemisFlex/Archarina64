using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina
{
    [Serializable]
    public struct DungeonGridPoint : IEquatable<DungeonGridPoint>
    {
        public int X, Y;
        public DungeonGridPoint(int x, int y) { X = x; Y = y; }
        public bool Equals(DungeonGridPoint other) { return X == other.X && Y == other.Y; }
        public override bool Equals(object obj) { return obj is DungeonGridPoint && Equals((DungeonGridPoint)obj); }
        public override int GetHashCode() { return (X * 397) ^ Y; }
        public override string ToString() { return X + "," + Y; }
    }

    [Serializable]
    public sealed class DungeonPushBlockPuzzle
    {
        public int Width, Height;
        public DungeonGridPoint PlayerStart;
        public List<DungeonGridPoint> Walls = new List<DungeonGridPoint>();
        public List<DungeonGridPoint> Blocks = new List<DungeonGridPoint>();
        public List<DungeonGridPoint> Goals = new List<DungeonGridPoint>();
    }

    [Serializable]
    public sealed class DungeonPushBlockReport
    {
        public bool Solvable;
        public int ExploredStates, Pushes, WalkSteps;
        public List<string> Errors = new List<string>();
        public List<DungeonGridPoint> PushRoute = new List<DungeonGridPoint>();
    }

    public static class DungeonPushBlockSolver
    {
        private sealed class State
        {
            public DungeonGridPoint Player;
            public List<DungeonGridPoint> Blocks;
            public State Parent;
            public DungeonGridPoint PushedBlock;
            public int PushCount;
            public int WalkCount;
        }

        public static DungeonPushBlockReport Solve(DungeonPushBlockPuzzle puzzle)
        {
            DungeonPushBlockReport result = new DungeonPushBlockReport();
            if (puzzle == null) { result.Errors.Add("A push-block puzzle is required."); return result; }
            if (puzzle.Width < 1 || puzzle.Height < 1) { result.Errors.Add("Push-block puzzle dimensions are invalid."); return result; }
            if (puzzle.Blocks.Count == 0 || puzzle.Goals.Count < puzzle.Blocks.Count) { result.Errors.Add("The puzzle needs at least one block and one goal per block."); return result; }
            HashSet<DungeonGridPoint> walls = new HashSet<DungeonGridPoint>(puzzle.Walls);
            if (!Inside(puzzle, puzzle.PlayerStart) || walls.Contains(puzzle.PlayerStart)) { result.Errors.Add("The player start is not walkable."); return result; }
            if (puzzle.Blocks.Any(block => !Inside(puzzle, block) || walls.Contains(block) || puzzle.Blocks.Count(b => b.Equals(block)) > 1))
            { result.Errors.Add("Blocks must be unique and placed on walkable cells."); return result; }
            if (puzzle.Goals.Any(goal => !Inside(puzzle, goal) || walls.Contains(goal))) { result.Errors.Add("Goals must be inside the puzzle and outside walls."); return result; }

            State start = new State { Player = puzzle.PlayerStart, Blocks = Normalize(puzzle.Blocks) };
            Queue<State> queue = new Queue<State>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(start); visited.Add(Key(start));
            while (queue.Count > 0)
            {
                State current = queue.Dequeue(); result.ExploredStates++;
                if (current.Blocks.All(block => puzzle.Goals.Contains(block)))
                {
                    result.Solvable = true; result.Pushes = current.PushCount; result.WalkSteps = current.WalkCount;
                    for (State cursor = current; cursor != null && cursor.PushedBlock.X != int.MinValue; cursor = cursor.Parent)
                        result.PushRoute.Add(cursor.PushedBlock);
                    result.PushRoute.Reverse();
                    return result;
                }
                foreach (DungeonGridPoint direction in Directions())
                {
                    DungeonGridPoint next = Add(current.Player, direction);
                    if (!Inside(puzzle, next) || walls.Contains(next)) continue;
                    int blockIndex = current.Blocks.IndexOf(next);
                    if (blockIndex < 0)
                    {
                        Enqueue(queue, visited, current, next, current.Blocks, null, current.PushCount, current.WalkCount + 1);
                        continue;
                    }
                    DungeonGridPoint beyond = Add(next, direction);
                    if (!Inside(puzzle, beyond) || walls.Contains(beyond) || current.Blocks.Contains(beyond)) continue;
                    List<DungeonGridPoint> moved = current.Blocks.ToList(); moved[blockIndex] = beyond;
                    Enqueue(queue, visited, current, next, moved, beyond, current.PushCount + 1, current.WalkCount + 1);
                }
            }
            result.Errors.Add("No valid push-block state sequence reaches every goal.");
            return result;
        }

        private static void Enqueue(Queue<State> queue, HashSet<string> visited, State parent, DungeonGridPoint player,
            List<DungeonGridPoint> blocks, DungeonGridPoint? pushed, int pushes, int walks)
        {
            State next = new State { Player = player, Blocks = Normalize(blocks), Parent = parent,
                PushedBlock = pushed ?? new DungeonGridPoint(int.MinValue, int.MinValue), PushCount = pushes, WalkCount = walks };
            if (visited.Add(Key(next))) queue.Enqueue(next);
        }

        private static List<DungeonGridPoint> Normalize(IEnumerable<DungeonGridPoint> points)
        {
            return points.OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
        }

        private static string Key(State state)
        {
            return state.Player + "|" + string.Join(";", state.Blocks.Select(point => point.ToString()).ToArray());
        }

        private static bool Inside(DungeonPushBlockPuzzle puzzle, DungeonGridPoint point)
        {
            return point.X >= 0 && point.X < puzzle.Width && point.Y >= 0 && point.Y < puzzle.Height;
        }

        private static DungeonGridPoint Add(DungeonGridPoint left, DungeonGridPoint right)
        {
            return new DungeonGridPoint(left.X + right.X, left.Y + right.Y);
        }

        private static IEnumerable<DungeonGridPoint> Directions()
        {
            yield return new DungeonGridPoint(1, 0);
            yield return new DungeonGridPoint(-1, 0);
            yield return new DungeonGridPoint(0, 1);
            yield return new DungeonGridPoint(0, -1);
        }
    }
}
