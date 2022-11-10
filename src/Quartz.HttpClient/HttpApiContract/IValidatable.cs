namespace Quartz.HttpApiContract;

internal interface IValidatable
{
    IEnumerable<string> Validate();
}