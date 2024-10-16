﻿using NexusMods.DataModel.JsonConverters.ExpressionGenerator;

namespace NexusMods.Games.TestHarness;

public class TypeFinder : ITypeFinder
{
    public IEnumerable<Type> DescendentsOf(Type type)
    {
        return AllTypes.Where(t => t.IsAssignableTo(type));
    }

    private IEnumerable<Type> AllTypes => new[]
    {
        typeof(NexusModFile),
    };
}