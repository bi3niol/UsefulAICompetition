using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;

public class SenderTools(PostCommentTool postComment)
{
    public IEnumerable<AITool> GetAll() =>
    [
        AITool.FromMethod(postComment.PostCommentToWorkItemAsync)
    ];
}
