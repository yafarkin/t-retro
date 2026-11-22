using Domain;

namespace Domain.Tests;

public sealed class RequiredTextTests
{
    [Fact]
    public void CreateTrimsText()
    {
        var result = RequiredText.Create("  Сотрудник А  ", 120);

        Assert.Equal("Сотрудник А", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsEmptyText(string? value)
    {
        var exception = Assert.Throws<DomainValidationException>(() => RequiredText.Create(value, 120));

        Assert.Equal("TextRequired", exception.Code);
    }

    [Fact]
    public void CreateRejectsLongText()
    {
        var exception = Assert.Throws<DomainValidationException>(() => RequiredText.Create("12345", 4));

        Assert.Equal("TextTooLong", exception.Code);
    }
}
