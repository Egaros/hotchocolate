using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Stitching.Utilities;

namespace HotChocolate.Stitching.Client
{
    internal class RemoteRequestDispatcher
    {
        private readonly IServiceProvider _services;
        private readonly IQueryExecutor _queryExecutor;

        public RemoteRequestDispatcher(
            IServiceProvider services,
            IQueryExecutor queryExecutor)
        {
            _services = services
                ?? throw new ArgumentNullException(nameof(services));
            _queryExecutor = queryExecutor
                ?? throw new ArgumentNullException(nameof(queryExecutor));
        }

        public Task DispatchAsync(
            IList<BufferedRequest> requests,
            CancellationToken cancellationToken)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (requests.Count == 0)
            {
                return Task.CompletedTask;
            }

            if (requests.Count == 1)
            {
                return DispatchSingleRequestsAsync(requests, cancellationToken);
            }

            var rewriter = new MergeQueryRewriter();
            var variableValues = new Dictionary<string, object>();

            var operationNames = requests
                .Select(r => r.Request.OperationName)
                .Where(n => n != null)
                .Distinct()
                .ToList();

            if (operationNames.Count == 1)
            {
                rewriter.SetOperationName(new NameNode(operationNames[0]));
            }

            for (int i = 0; i < requests.Count; i++)
            {
                MergeRequest(requests[i], rewriter, variableValues, $"__{i}_");
            }

            return DispatchRequestsAsync(
                requests,
                rewriter.Merge(),
                rewriter.OperationName,
                variableValues,
                cancellationToken);
        }

        private async Task DispatchSingleRequestsAsync(
            IList<BufferedRequest> requests,
            CancellationToken cancellationToken)
        {
            try
            {
                var builder = QueryRequestBuilder.From(requests[0].Request);
                builder.SetServices(_services);

                var result = (IReadOnlyQueryResult)await _queryExecutor
                    .ExecuteAsync(builder.Create(), cancellationToken)
                    .ConfigureAwait(false);

                requests[0].Promise.SetResult(result);
            }
            catch (Exception ex)
            {
                requests[0].Promise.SetException(ex);
            }
        }

        private async Task DispatchRequestsAsync(
            IList<BufferedRequest> requests,
            DocumentNode mergedQuery,
            NameNode operationName,
            IReadOnlyDictionary<string, object> variableValues,
            CancellationToken cancellationToken)
        {
            int index = 0;
            try
            {
                IReadOnlyQueryRequest mergedRequest =
                    QueryRequestBuilder.New()
                        .SetQuery(mergedQuery)
                        .SetOperation(operationName.Value)
                        .SetVariableValues(variableValues)
                        .SetServices(_services)
                        .Create();

                var mergedResult = (IReadOnlyQueryResult)await _queryExecutor
                    .ExecuteAsync(mergedRequest, cancellationToken)
                    .ConfigureAwait(false);
                var handledErrors = new HashSet<IError>();

                for (int i = 0; i < requests.Count; i++)
                {
                    index = i;

                    QueryResultBuilder result = ExtractResult(
                        requests[i].Aliases,
                        mergedResult,
                        handledErrors);

                    IReadOnlyList<IError> mergedErrors = mergedResult.Errors ?? Array.Empty<IError>();

                    if (handledErrors.Count < mergedErrors.Count
                        && i == requests.Count - 1)
                    {
                        foreach (IError error in mergedErrors.Except(handledErrors))
                        {
                            result.AddError(error);
                        }
                    }

                    requests[i].Promise.SetResult(result.Create());
                }
            }
            catch (Exception ex)
            {
                for (int i = index; i < requests.Count; i++)
                {
                    requests[i].Promise.SetException(ex);
                }
            }
        }

        private static void MergeRequest(
            BufferedRequest bufferedRequest,
            MergeQueryRewriter rewriter,
            IDictionary<string, object> variableValues,
            NameString requestPrefix)
        {
            MergeVariables(
                bufferedRequest.Request.VariableValues,
                variableValues,
                requestPrefix);

            bool isAutoGenerated = bufferedRequest.Request.Properties != null
                && bufferedRequest.Request.Properties.ContainsKey(
                    WellKnownProperties.IsAutoGenerated);

            bufferedRequest.Aliases = rewriter.AddQuery(
                bufferedRequest.Document,
                requestPrefix,
                isAutoGenerated);
        }

        private static void MergeVariables(
            IReadOnlyDictionary<string, object> original,
            IDictionary<string, object> merged,
            NameString requestPrefix)
        {
            if (original != null)
            {
                foreach (KeyValuePair<string, object> item in original)
                {
                    string variableName = MergeUtils.CreateNewName(
                        item.Key, requestPrefix);
                    merged.Add(variableName, item.Value);
                }
            }
        }

        private static QueryResultBuilder ExtractResult(
            IDictionary<string, string> aliases,
            IReadOnlyQueryResult mergedResult,
            ICollection<IError> handledErrors)
        {
            var result = QueryResultBuilder.New();
            var data = new OrderedDictionary();

            foreach (KeyValuePair<string, string> alias in aliases)
            {
                if (mergedResult.Data.TryGetValue(alias.Key, out object o))
                {
                    data.Add(alias.Value, o);
                }
            }

            result.SetData(data);

            if (mergedResult.Errors is { })
            {
                foreach (IError error in mergedResult.Errors)
                {
                    if (TryResolveField(error, aliases, out string responseName))
                    {
                        var path = new List<object>();
                        path.Add(responseName);
                        if (error.Path.Count > 1)
                        {
                            path.AddRange(error.Path.Skip(1));
                        }

                        handledErrors.Add(error);
                        result.AddError(RewriteError(error, responseName));
                    }
                }
            }

            if (mergedResult.ContextData is { })
            {
                foreach (KeyValuePair<string, object> item in mergedResult.ContextData)
                {
                    result.SetContextData(item.Key, item.Value);
                }
            }

            return result;
        }

        private static IError RewriteError(IError error, string responseName)
        {
            var path = new List<object>();
            path.Add(responseName);
            if (error.Path.Count > 1)
            {
                path.AddRange(error.Path.Skip(1));
            }
            return error.WithPath(path);
        }

        private static bool TryResolveField(
            IError error,
            IDictionary<string, string> aliases,
            out string responseName)
        {
            if (error.Path != null)
            {
                string rootField = error.Path.FirstOrDefault()?.ToString();
                if (rootField != null
                    && aliases.TryGetValue(rootField, out string s))
                {
                    responseName = s;
                    return true;
                }
            }

            responseName = null;
            return false;
        }
    }
}
