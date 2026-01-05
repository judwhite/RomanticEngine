namespace RomanticEngine.Core;

public static class Configuration
{
    public static class Evaluation
    {
        public static bool EnableMaterial { get; set; } = true;
        public static bool EnableRMobility { get; set; } = true;
        public static bool EnableKingSafety { get; set; } = true;

        public static int MaterialWeight { get; set; } = 1;
        public static int MobilityWeight { get; set; } = 10;
        public static int KingSafetyWeight { get; set; } = 20;
    }

    public static class Standard
    {
        public static int Hash { get; set; } = 16;
        public static bool Ponder { get; set; } = false;
        public static int MultiPV { get; set; } = 1;
        public static string DebugLogFile { get; set; } = "";
        
        // Additional Standard Options
        public static int Threads { get; set; } = 1;
        public static int MoveOverhead { get; set; } = 10;
        public static string SyzygyPath { get; set; } = "";
    }
}
