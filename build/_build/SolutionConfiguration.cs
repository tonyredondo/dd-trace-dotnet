public class SolutionConfiguration
{
    public string Value { get; }
    public SolutionConfiguration(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;

    public static implicit operator string(SolutionConfiguration config)
    {
        return config.Value;
    }
}
