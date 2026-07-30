using System.Reflection;

using Newtonsoft.Json.Serialization;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// Teaches the serializer to build a <see cref="Key{T}" /> through its <c>(name, group)</c> constructor.
/// </summary>
/// <remarks>
/// <para>
/// Keys are immutable and have two public constructors, and the default resolver only binds a parameterized
/// constructor when a type has exactly one — so without this, reading a key fails with "unable to find a
/// constructor to use". Naming the constructor here rather than converting the type keeps the JSON exactly
/// as it was when keys were built by populating properties, so a payload written by an earlier version still
/// reads and one written now is still readable by it.
/// </para>
/// <para>
/// This is a resolver rather than a <c>JsonConverter</c> on purpose: a converter registered on the serializer
/// is not consulted for a value whose type came from a <c>$type</c> property — a key held in a job data map,
/// say — and the constructor named here is used on every path.
/// </para>
/// </remarks>
internal sealed class ImmutableKeyContractResolver : DefaultContractResolver
{
    public ImmutableKeyContractResolver()
    {
        IgnoreSerializableInterface = true;
    }

    protected override JsonObjectContract CreateObjectContract(Type objectType)
    {
        JsonObjectContract contract = base.CreateObjectContract(objectType);

        ConstructorInfo? constructor = FindKeyConstructor(objectType);
        if (constructor is not null)
        {
            contract.OverrideCreator = arguments => constructor.Invoke(arguments);
            contract.CreatorParameters.Clear();
            foreach (JsonProperty parameter in CreateConstructorParameters(constructor, contract.Properties))
            {
                contract.CreatorParameters.Add(parameter);
            }
        }

        return contract;
    }

    private static ConstructorInfo? FindKeyConstructor(Type objectType)
    {
        for (Type? type = objectType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Key<>))
            {
                return objectType.GetConstructor([typeof(string), typeof(string)]);
            }
        }

        return null;
    }
}
