using Xunit;

namespace EnglishStudio.Integration.Tests.Infrastructure;

/// <summary>
/// Serializes every test that touches <c>DictionaryPaths.AppDataRootOverride</c> — a global static
/// that reroutes content/DB paths to a temp folder. Running these in parallel would let one test's
/// override leak into another. See plans/Infra_Publish_GitHub_AgentExecution.md §1.3, §A7.
/// </summary>
[CollectionDefinition("ContentIO", DisableParallelization = true)]
public sealed class ContentIoCollection;
