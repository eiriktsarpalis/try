﻿using Octokit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MLS.Repositories
{
    public class RepoLocator : IRepoLocator
    {
        public async Task<IEnumerable<Repo>> LocateRepo(string repo)
        {
            var req = new SearchRepositoriesRequest(repo);

            // TODO: Should we be forcing users to log in (and thereby use their own API token)?
            var client = new GitHubClient(new ProductHeaderValue("github-try-demo"));

            var result = await client.Search.SearchRepo(req);
            return result.Items.Select(i => new Repo(i.FullName, i.CloneUrl));
        }
    }
}
