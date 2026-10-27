using EirService.Sessions.Application.Features.Teacher.ArchiveSession;

namespace EirService.Test;

public sealed class ArchiveSessionValidationTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("Lesson 1", true)]
    [InlineData("  Lesson 1  ", true)]
    [InlineData("", false)]
    [InlineData(" \t\n ", false)]
    public void OptionalTitleMustContainTextWhenProvided(string? title, bool valid)
    {
        var command = new ArchiveSessionCommand(Guid.NewGuid(), "teacher", 1, title);

        Assert.Equal(valid, new ArchiveSessionCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void TitleLengthIsValidatedAfterTrimming()
    {
        var validator = new ArchiveSessionCommandValidator();
        var command = new ArchiveSessionCommand(Guid.NewGuid(), "teacher", 1, " " + new string('x', 200) + " ");

        Assert.True(validator.Validate(command).IsValid);
        Assert.False(validator.Validate(command with { Title = new string('x', 201) }).IsValid);
    }
}
