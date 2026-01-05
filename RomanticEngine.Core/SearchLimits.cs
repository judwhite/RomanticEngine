using System;

namespace RomanticEngine.Core;

public class SearchLimits
{
    public bool Infinite { get; set; }
    public bool Ponder { get; set; }
    
    // Time management
    public int WhiteTime { get; set; }
    public int BlackTime { get; set; }
    public int WhiteIncrement { get; set; }
    public int BlackIncrement { get; set; }
    public int MoveTime { get; set; }
    public int MovesToGo { get; set; }
    
    // Depth/Nodes
    public int Depth { get; set; }
    public long Nodes { get; set; }
    public int Mate { get; set; }

    // Search moves
    public string[]? SearchMoves { get; set; }
}
