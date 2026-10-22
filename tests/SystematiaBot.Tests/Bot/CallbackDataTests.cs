using System.Text;
using Shouldly;
using SystematiaBot.Bot.Routing;

namespace SystematiaBot.Tests.Bot;

public class CallbackDataTests
{
    public static TheoryData<CallbackData, string> Samples => new()
    {
        { new CallbackData("n", "pk", 123), "n:pk:123" },
        { new CallbackData("n", "rj", 123, "old"), "n:rj:123:old" },
        { new CallbackData("n", "pg", 2), "n:pg:2" },
        { new CallbackData("s", "mode", null, "q"), "s:mode::q" },
        { new CallbackData("m", "main"), "m:main" }
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Serializes_and_round_trips(CallbackData data, string expected)
    {
        var serialized = data.ToString();

        serialized.ShouldBe(expected);
        CallbackData.TryParse(serialized, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(data);
    }

    [Fact]
    public void Worst_case_payload_fits_in_64_bytes()
    {
        var data = new CallbackData("n", "rj", int.MaxValue, "custom");

        Encoding.UTF8.GetByteCount(data.ToString()).ShouldBeLessThanOrEqualTo(CallbackData.MaxBytes);
    }

    [Fact]
    public void Too_long_payload_throws()
    {
        Should.Throw<InvalidOperationException>(() => new CallbackData("n", "x", 1, new string('a', 64)).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("n")]
    [InlineData(":pk")]
    [InlineData("n:pk:abc")]
    public void Invalid_payload_is_rejected(string? value)
    {
        CallbackData.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Arg_may_contain_colons()
    {
        CallbackData.TryParse("n:x:1:a:b", out var data).ShouldBeTrue();
        data.Arg.ShouldBe("a:b");
    }
}
