using Xunit;

namespace CubaseInspector.Tests;

/// <summary>Private fixtures are opt-in; absence must not be reported as a passing check.</summary>
public sealed class PrivateCprFixtureFactAttribute : FactAttribute
{
    public PrivateCprFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CPE_CPR_FIXTURE_DIR")))
            Skip = "Set CPE_CPR_FIXTURE_DIR to the controlled, hash-verified private CPR fixtures.";
    }
}
