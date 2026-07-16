using Xunit;

// The CLI reads process-global state that no seam can scope to one test: P27_SERVER and
// friends (Environment), and RemoteClient.HandlerFactory. Server-mode and completion
// tests set those, so a parallel class would observe another's server. The suite is a
// couple of seconds either way; correctness is worth more than the concurrency.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
