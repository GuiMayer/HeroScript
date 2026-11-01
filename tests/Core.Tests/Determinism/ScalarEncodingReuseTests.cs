using System.Text;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class ScalarEncodingReuseTests
{
    [Fact]
    public void ScalarCachePreservesSignedZeroScaleNullableTypesAndEscaping()
    {
        using var scope = CanonicalJson.BeginHashScope();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            Check(0f); Check(-0f); Check(0d); Check(-0d);
            Check(1m); Check(1.0m); Check(1.00m); Check(new decimal(0, 0, 0, true, 2));
            Check(1); Check(1L); Check((byte)1); Check((float?)1); Check((float?)null);
            Check(true); Check(false); Check('a'); Check("á<>\\\"\uD800");
            Check(Guid.Parse("9a4a1269-a313-438a-a3c1-59994468e001"));
        }
        // Eviction remains observationally irrelevant.
        for (var index = 0; index < 1000; index++) Check(index);
        Check(-0f); Check(1.00m);
    }
    private static void Check<T>(T value) => Assert.Equal(CanonicalJson.Serialize(value),
        Encoding.UTF8.GetString(CanonicalJson.Property("value", value).Bytes.Span));
}
