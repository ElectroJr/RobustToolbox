﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Console;

namespace Robust.Shared.Toolshed.Syntax;

/// <summary>
/// Interface for attempting to infer the type of a variable while parsing toolshed commands.
/// </summary>
/// <remarks>
/// The variable parser being used by the <see cref="ParserContext"/> may change depending on which command/block is
/// currently being parsed. E.g., if a command has a variable confined to a command block, it might use a <see cref="LocalVarParser"/>
/// </remarks>
public interface IVariableParser
{
    public static readonly IVariableParser Empty = new EmptyVarParser();

    /// <summary>
    /// Attempt to get the type of the variable with the given name.
    /// </summary>
    bool TryParseVar(string name, [NotNullWhen(true)] out Type? type);

    /// <summary>
    /// Generate completion options containing valid variable names along with their types.
    /// </summary>
    CompletionResult GenerateCompletions()
        => CompletionResult.FromHintOptions(
            GetVars().Select(x => new CompletionOption($"${x.Item1}", $"({x.Item2})")),
            "<variable name>");

    /// <summary>
    /// Generate completion options containing valid variable names along with their types.
    /// </summary>
    CompletionResult GenerateCompletions<T>()
        => CompletionResult.FromHintOptions(
            GetVars()
                .Where(x => x.Item2 == typeof(T))
                .Select(x => new CompletionOption($"${x.Item1}")),
            $"<Variable of type {typeof(T).PrettyName()}>");

    public IEnumerable<(string, Type)> GetVars();

    private sealed class EmptyVarParser : IVariableParser
    {
        public bool TryParseVar(string name, [NotNullWhen(true)] out Type? type)
        {
            type = null;
            return false;
        }

        public IEnumerable<(string, Type)> GetVars()
        {
            yield break;
        }
    }
}

/// <summary>
/// Infer the variable type from the value currently saved to an invocation context.
/// This is only valid if no other command that has been parsed so far could modify the stored value once invoked.
/// If a command can modify the variable's type, it should instead use a <see cref="LocalVarParser"/>.
/// </summary>
public sealed class InvocationCtxVarParser(IInvocationContext ctx) : IVariableParser
{
    private readonly IInvocationContext _ctx = ctx;

    public bool TryParseVar(string name, [NotNullWhen(true)] out Type? type)
    {
        type = _ctx.ReadVar(name)?.GetType();
        return type != null;
    }

    public IEnumerable<(string, Type)> GetVars()
    {
        foreach (var name in _ctx.GetVars())
        {
            if (TryParseVar(name, out var type))
                yield return (name, type);
        }
    }
}

/// <summary>
/// Simple wrapper around a variable type parser that modifies / overrides the types returned by some other parser.
/// </summary>
public sealed class LocalVarParser(IVariableParser inner) : IVariableParser
{
    public readonly IVariableParser Inner = inner;

    public Dictionary<string, Type>? Variables;

    public void SetLocalType(string name, Type? type)
    {
        if (type == null)
        {
            Variables?.Remove(name);
            return;
        }

        Variables ??= new();
        Variables[name] = type;
    }

    public bool TryParseVar(string name, [NotNullWhen(true)] out Type? type)
    {
        if (Variables != null && Variables.TryGetValue(name, out type))
            return true;

        return Inner.TryParseVar(name, out type);
    }

    public IEnumerable<(string, Type)> GetVars()
    {
        foreach (var (name, type) in Variables!)
        {
            yield return (name, type);
        }

        foreach (var (name, type) in Inner.GetVars())
        {
            if (!Variables.ContainsKey(name))
                yield return (name, type);
        }
    }
}