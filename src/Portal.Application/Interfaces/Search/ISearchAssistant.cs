using Portal.Application.DTOs.Search;
namespace Portal.Application.Interfaces.Search;

public interface ISearchAssistant
{
    SearchIntentDto Interpret(string? question);
}
