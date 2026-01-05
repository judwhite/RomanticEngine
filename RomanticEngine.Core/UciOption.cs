namespace RomanticEngine.Core;

public enum UciOptionType
{
    Check,
    Spin,
    Combo,
    Button,
    String
}

public class UciOption
{
    public string Name { get; set; } = "";
    public UciOptionType Type { get; set; }
    public string DefaultValue { get; set; } = "";
    public string CurrentValue { get; set; } = "";
    public int? Min { get; set; }
    public int? Max { get; set; }
    public List<string> Var { get; set; } = [];
    public Action<string>? OnChanged { get; set; }

    public override string ToString()
    {
        string baseStr = $"option name {Name} type {Type.ToString().ToLower()}";

        switch (Type)
        {
            case UciOptionType.Check:
                return $"{baseStr} default {DefaultValue.ToLower()}";
            case UciOptionType.Spin:
                return $"{baseStr} default {DefaultValue} min {Min} max {Max}";
            case UciOptionType.Combo:
                string vars = "";
                foreach (var v in Var)
                    vars += $" var {v}";
                return $"{baseStr} default {DefaultValue}{vars}";
            case UciOptionType.String:
                return $"{baseStr} default {DefaultValue}";
            case UciOptionType.Button:
                return baseStr;
            default:
                return baseStr;
        }
    }
}
