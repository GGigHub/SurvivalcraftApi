using System;

namespace Survivalcraft.Test;

internal readonly record struct CommandResult(string Raw)
{
	public bool Succeeded => Raw.Contains("result=Success", StringComparison.OrdinalIgnoreCase);

	public bool IsValid => Raw.Contains("valid=True", StringComparison.OrdinalIgnoreCase);
}


