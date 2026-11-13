using EirService.Participants.Domain.Entities;
using Shared.Domain.ValueObjects;

namespace EirService.Test;

public sealed class StudentIdentityTransferDomainTests
{
    [Fact]
    public void CreateKeepsTheIdentityAndExpiresAfterTenMinutesInUtc()
    {
        var now = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.FromHours(2));
        var studentId = Guid.NewGuid().ToString("N");

        var transfer = StudentIdentityTransfer.Create(studentId, now);

        Assert.Equal(studentId, transfer.StudentId);
        Assert.True(ShortCode.IsValid(transfer.Code));
        Assert.Equal(transfer.Code, transfer.Id);
        Assert.Equal(now.AddMinutes(10), transfer.ExpiresAtUtc);
        Assert.Equal(TimeSpan.Zero, transfer.ExpiresAtUtc.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateRequiresAStudentIdentity(string? studentId)
    {
        Assert.ThrowsAny<ArgumentException>(() => StudentIdentityTransfer.Create(studentId!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateRejectsAnIdentityThatCannotBePersisted()
    {
        Assert.Throws<ArgumentException>(() => StudentIdentityTransfer.Create(new string('a', 101), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SharedCodeNormalizesCaseAndWhitespaceWithValueEquality()
    {
        var code = new ShortCode(" abcd2345 ");

        Assert.Equal("ABCD2345", code.Value);
        Assert.Equal(new ShortCode("ABCD2345"), code);
        Assert.True(ShortCode.IsValid(" abcd2345 "));
        Assert.Equal(ShortCode.Length, ShortCode.Generate().Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ABCD234")]
    [InlineData("ABCD23456")]
    [InlineData("ABCD01IO")]
    [InlineData("ABCD-234")]
    public void SharedCodeRejectsInvalidLengthAndAmbiguousCharacters(string? value)
    {
        Assert.False(ShortCode.IsValid(value));
        Assert.ThrowsAny<ArgumentException>(() => new ShortCode(value!));
    }
}
