namespace CodeReviewerAgent.Core
{
    public interface ILlmClient
    {
        MessageResponse Request(object requestBody);
    }
}
