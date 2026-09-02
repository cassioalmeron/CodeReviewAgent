namespace CodeReviewerAgent.Core.Llm;

public interface ILlmClient
{
    MessageResponse Request(object requestBody);
}
