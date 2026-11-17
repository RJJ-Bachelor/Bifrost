using System.Collections.Concurrent;
using System.Reflection;

namespace Shared.Application.Messaging;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MessageContractAttribute(string name, int version = 1) : Attribute
{
    public string Name { get; } = name;
    public int Version { get; } = version;
}

public static class MessageContracts
{
    private static readonly ConcurrentDictionary<Type, MessageContractAttribute> Contracts = new();

    public static MessageContractAttribute For(Type type) => Contracts.GetOrAdd(type,
        value => value.GetCustomAttribute<MessageContractAttribute>() ?? new(value.FullName!));

    public static bool Accepts<T>(MessageMetadata? metadata) => metadata is null ||
        (metadata.Type == For(typeof(T)).Name || metadata.Type == typeof(T).FullName) &&
        metadata.Version == For(typeof(T)).Version;
}
