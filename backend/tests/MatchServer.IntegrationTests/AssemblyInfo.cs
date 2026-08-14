// Integration tests run SEQUENTIALLY.
//
// Every test class boots its own ASP.NET Core server, each with a 30Hz match loop. Running eight
// of those in parallel starves the loops: match time advances a fixed 33ms per tick, so a starved
// loop makes 8 seconds of match time take far longer in wall-clock, and any test waiting on a
// game-time deadline flakes for reasons that have nothing to do with the rule under test.
//
// Sequential execution costs roughly 20s for the suite and removes that whole class of flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
