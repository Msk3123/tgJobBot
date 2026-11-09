using Shouldly;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class SkillGroundingTests
{
    private const string Profile = "C#, ASP.NET Core, EF Core, PostgreSQL, MongoDB, Docker, React, TypeScript, JavaScript, xUnit";

    [Fact]
    public void Technology_missing_from_profile_is_reported()
    {
        SkillGrounding.FindUngrounded("I built services with ASP.NET Core and Kafka on AWS.", Profile)
            .ShouldBe(["Kafka", "AWS"], ignoreOrder: true);
    }

    [Fact]
    public void Grounded_draft_has_no_warnings()
    {
        SkillGrounding.FindUngrounded("Shipped a React + TypeScript admin on top of PostgreSQL and Docker.", Profile).ShouldBeEmpty();
    }

    [Fact]
    public void JavaScript_does_not_count_as_Java()
    {
        SkillGrounding.FindUngrounded("Frontend in JavaScript.", Profile).ShouldBeEmpty();
        SkillGrounding.FindUngrounded("Backend in Java.", Profile).ShouldBe(["Java"]);
    }

    [Fact]
    public void Aliases_and_case_are_tolerated()
    {
        SkillGrounding.FindUngrounded("react.js and postgres", Profile).ShouldBeEmpty();
    }
}
