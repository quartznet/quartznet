using BenchmarkDotNet.Attributes;
using Quartz.Impl.Matchers;

namespace Quartz.Benchmark;

public class StringOperatorBenchmark
{
    [Benchmark]
    public bool Equals_Object_SameReference()
    {
        object equality = StringOperator.Equality;

        return StringOperator.Equality.Equals(equality);
    }

    [Benchmark]
    public bool Equals_Object_NotEqual()
    {
        object equality = StringOperator.Equality;

        return StringOperator.EndsWith.Equals(equality);
    }

    [Benchmark]
    public bool Equals_StringOperator_SameReference()
    {
        var equality = StringOperator.Equality;

        return equality.Equals(equality);
    }

    [Benchmark]
    public bool Equals_StringOperator_NotEqual()
    {
        var equality = StringOperator.Equality;

        return StringOperator.EndsWith.Equals(equality);
    }
}