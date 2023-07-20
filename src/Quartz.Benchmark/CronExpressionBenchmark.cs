using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class CronExpressionBenchmark
{
    [ParamsSource(nameof(CronExpressionValues))]
    public string CronExpression { get; set; } = "";

    public IEnumerable<string> CronExpressionValues => new[]
    {
        "0 15 10 * * ?",
        "0 0/5 10 6,15 * ? *",
        "0 15 10 15 * ? *",
        "0 15 10 15,31 * ? *",
        "0 15 10 6,15,LW * ? *",
        "0 15 10 15,31 * ? *",
        "0 15 10 15,L-2 * ? *",
        "0 15 10 1,2,3,4,5,6,7,8,9,10,15,L * ? *",
        "0 15 10 15,LW-2 * ? *",
        "0 15 10 ? * 6#3 *"
    };

    [Benchmark]
    public void CreateExpressionsAndCalculateFireTimeAfter()
    {
        var expression = new CronExpression(CronExpression);
        expression.GetNextValidTimeAfter(new DateTime(2005, 6, 1, 22, 15, 0));
        expression.GetNextValidTimeAfter(new DateTime(2005, 12, 31, 23, 59, 59));
    }
}