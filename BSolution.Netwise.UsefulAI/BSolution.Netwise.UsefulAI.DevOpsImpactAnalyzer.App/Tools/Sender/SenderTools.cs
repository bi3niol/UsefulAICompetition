using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;

public class SenderTools(PostCommentTool postComment)
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(postComment.PostCommentToWorkItemAsync)
    ];
}
