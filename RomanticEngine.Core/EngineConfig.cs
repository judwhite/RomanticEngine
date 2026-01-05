namespace RomanticEngine.Core;

public class EngineConfig
{
    public EvaluationConfig Evaluation { get; } = new();
    public StandardConfig Standard { get; } = new();

    public class EvaluationConfig
    {
        public bool EnableMaterial { get; set; } = true;
        public bool EnableRMobility { get; set; } = true;
        public bool EnableKingSafety { get; set; } = true;

        public int MaterialWeight { get; set; } = 1;
        public int MobilityWeight { get; set; } = 10;
        public int KingSafetyWeight { get; set; } = 20;
    }

    public class StandardConfig
    {
        public int Hash { get; set; } = 16;
        public bool Ponder { get; set; }
        public int MultiPV { get; set; } = 1;
        public string DebugLogFile { get; set; } = "";
        public int Threads { get; set; } = 1;
        public bool DebugEnabled { get; set; }
        public int MoveOverhead { get; set; } = 10;
        public string SyzygyPath { get; set; } = "";
    }
}
