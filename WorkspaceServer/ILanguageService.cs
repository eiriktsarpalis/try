﻿using System.Threading.Tasks;
using Clockwise;
using WorkspaceServer.Models;
using WorkspaceServer.Models.Completion;
using WorkspaceServer.Models.SignatureHelp;

namespace WorkspaceServer
{
    public interface ILanguageService
    {
        Task<CompletionResult> GetCompletionList(WorkspaceRequest request, Budget budget = null);
        Task<SignatureHelpResult> GetSignatureHelp(WorkspaceRequest request, Budget budget = null);
    }
}