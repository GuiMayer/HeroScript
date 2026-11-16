using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using API.Contracts;
using Xunit;

namespace API.Tests.Authoring;

public sealed class CardAuthoringContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid ChangeId = Guid.Parse("e739926d-f0a5-4e69-8f38-94b9bce8f0d1");

    [Theory]
    [InlineData("""{"operation":"create","kind":"cards","definitionId":"demo","definition":{"cardId":"demo","tags":["new"]}}""", true)]
    [InlineData("""{"operation":"update","kind":"cards","definitionId":"demo","definition":{"cardId":"demo"}}""", true)]
    [InlineData("""{"operation":"delete","kind":"cards","definitionId":"demo"}""", true)]
    [InlineData("""{"operation":"update","kind":"cards","definitionId":"demo","definition":{"cardId":"different"}}""", false)]
    [InlineData("""{"operation":"delete","kind":"cards","definitionId":"demo","definition":{}}""", false)]
    [InlineData("""{"operation":"delete","kind":"card-selections","definitionId":"demo"}""", false)]
    [InlineData("""{"operation":99,"kind":"cards","definitionId":"demo"}""", false)]
    [InlineData("null", false)]
    public void ValidatesCardOperationEnvelope(string operation, bool expected)
    {
        var request = JsonSerializer.Deserialize<CardAuthoringChangeRequest>(
            $$"""{"changeId":"{{ChangeId}}","operations":[{{operation}}]}""", JsonOptions)!;

        Assert.Equal(expected, IsValid(request));
    }

    [Fact]
    public void AcceptsOneIntentAndOneOperationPerCard()
    {
        var target = Guid.Parse("7509e62b-f1ed-44aa-8842-b5b75b2cd160");
        var delete = new CardAuthoringOperation
        {
            Operation = CardAuthoringOperationType.Delete,
            Kind = "cards",
            DefinitionId = "demo"
        };
        var request = new CardAuthoringChangeRequest { ChangeId = ChangeId };
        Assert.True(IsValid(request with { UndoOf = target }));
        Assert.True(IsValid(request with { RedoOf = target }));
        Assert.False(IsValid(request with { UndoOf = target, RedoOf = target }));
        Assert.False(IsValid(request with { UndoOf = target, Operations = [delete] }));
        Assert.False(IsValid(request with { UndoOf = ChangeId }));
        Assert.False(IsValid(request with { Operations = [delete, delete] }));
        Assert.False(IsValid(request));
    }

    private static bool IsValid(CardAuthoringChangeRequest request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);
}
