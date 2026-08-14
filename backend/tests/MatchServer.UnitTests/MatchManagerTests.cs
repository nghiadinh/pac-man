using MatchServer.Engine;
using MatchServer.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchServer.UnitTests;

/// <summary>
/// Room allocation: auto-matching, room codes, and the concurrency guarantee that two players
/// joining at the same instant end up in the SAME room rather than two empty ones.
/// </summary>
public sealed class MatchManagerTests
{
    private static MatchManager NewManager() =>
        new(new MatchLogger(NullLogger<MatchLogger>.Instance));

    // ---- auto-matching ----

    [Fact]
    public void First_joiner_opens_a_room_as_the_runner()
    {
        var manager = NewManager();

        var outcome = manager.JoinOrCreate("conn-1");

        Assert.Equal(JoinStatus.Joined, outcome.Status);
        Assert.Equal(Role.Runner, outcome.Role);
        Assert.Equal(MatchStatus.WaitingForPlayers, outcome.Handle!.Locked(m => m.Status));
    }

    [Fact]
    public void Second_joiner_fills_the_same_room_as_the_hunter_and_starts_it()
    {
        var manager = NewManager();

        var first = manager.JoinOrCreate("conn-1");
        var second = manager.JoinOrCreate("conn-2");

        Assert.Equal(Role.Hunter, second.Role);
        Assert.Equal(first.Handle!.MatchId, second.Handle!.MatchId);
        Assert.Equal(MatchStatus.Active, second.Handle.Locked(m => m.Status));
    }

    [Fact]
    public void A_third_joiner_opens_a_new_room_rather_than_crowding_a_full_one()
    {
        var manager = NewManager();

        var first = manager.JoinOrCreate("conn-1");
        manager.JoinOrCreate("conn-2");
        var third = manager.JoinOrCreate("conn-3");

        Assert.Equal(Role.Runner, third.Role);
        Assert.NotEqual(first.Handle!.MatchId, third.Handle!.MatchId);
    }

    // ---- the race ----

    [Fact]
    public void Two_simultaneous_joins_land_in_one_room_not_two()
    {
        // The bug this guards against: scanning for an open room and creating one are a COMPOUND
        // operation. Without a gate around both, two callers can each see no open room, each
        // create their own, and both wait forever - while being each other's opponent.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var manager = NewManager();
            var ready = new ManualResetEventSlim(false);
            var outcomes = new JoinOutcome[2];

            var threads = Enumerable.Range(0, 2).Select(i => new Thread(() =>
            {
                ready.Wait();
                outcomes[i] = manager.JoinOrCreate($"conn-{i}");
            })).ToArray();

            foreach (var thread in threads) thread.Start();
            ready.Set(); // release both as close to simultaneously as the scheduler allows
            foreach (var thread in threads) thread.Join();

            Assert.Equal(
                outcomes[0].Handle!.MatchId,
                outcomes[1].Handle!.MatchId);

            // And exactly one of each role, rather than two Runners sitting in separate rooms.
            Assert.Equal(
                new[] { Role.Runner, Role.Hunter },
                outcomes.Select(o => o.Role).OrderBy(r => r).ToArray());
        }
    }

    [Fact]
    public void Many_simultaneous_joins_pair_up_completely()
    {
        // Eight players should become four full matches with nobody left waiting.
        var manager = NewManager();
        var ready = new ManualResetEventSlim(false);
        var outcomes = new JoinOutcome[8];

        var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
        {
            ready.Wait();
            outcomes[i] = manager.JoinOrCreate($"conn-{i}");
        })).ToArray();

        foreach (var thread in threads) thread.Start();
        ready.Set();
        foreach (var thread in threads) thread.Join();

        var rooms = outcomes.GroupBy(o => o.Handle!.MatchId).ToList();

        Assert.Equal(4, rooms.Count);
        Assert.All(rooms, room => Assert.Equal(2, room.Count()));
        Assert.All(rooms, room => Assert.Equal(
            MatchStatus.Active, room.First().Handle!.Locked(m => m.Status)));
    }

    // ---- room codes ----

    [Fact]
    public void A_room_code_is_four_unambiguous_characters()
    {
        var manager = NewManager();

        var code = manager.JoinOrCreate("conn-1").Handle!.MatchId;

        Assert.Equal(4, code.Length);
        // I/1 and O/0 are excluded so a code read aloud or off a screen is not misheard.
        Assert.DoesNotContain(code, c => c is 'I' or 'O' or '0' or '1');
    }

    [Fact]
    public void Two_players_using_the_same_code_meet_in_that_room()
    {
        var manager = NewManager();

        var first = manager.JoinOrCreate("conn-1", "WXYZ");
        var second = manager.JoinOrCreate("conn-2", "WXYZ");

        Assert.Equal("WXYZ", first.Handle!.MatchId);
        Assert.Equal(first.Handle.MatchId, second.Handle!.MatchId);
        Assert.Equal(Role.Runner, first.Role);
        Assert.Equal(Role.Hunter, second.Role);
    }

    [Fact]
    public void A_coded_room_is_not_handed_out_to_auto_matchers()
    {
        // Someone waiting in a private room should not have a stranger dropped into it.
        var manager = NewManager();

        manager.JoinOrCreate("conn-1", "WXYZ");
        var stranger = manager.JoinOrCreate("conn-2");

        Assert.NotEqual("WXYZ", stranger.Handle!.MatchId);
        Assert.Equal(Role.Runner, stranger.Role);
    }

    [Theory]
    [InlineData("wxyz")]
    [InlineData("  WXYZ  ")]
    [InlineData("WxYz")]
    public void Codes_are_case_insensitive_and_tolerate_stray_whitespace(string typed)
    {
        var manager = NewManager();
        manager.JoinOrCreate("conn-1", "WXYZ");

        var second = manager.JoinOrCreate("conn-2", typed);

        Assert.Equal(JoinStatus.Joined, second.Status);
        Assert.Equal(Role.Hunter, second.Role);
    }

    [Fact]
    public void Joining_a_full_room_is_refused_rather_than_silently_rerouted()
    {
        // Rerouting to some other room would be worse than an error: the player asked for a
        // specific opponent and would be quietly matched against a stranger instead.
        var manager = NewManager();
        manager.JoinOrCreate("conn-1", "WXYZ");
        manager.JoinOrCreate("conn-2", "WXYZ");

        var third = manager.JoinOrCreate("conn-3", "WXYZ");

        Assert.Equal(JoinStatus.RoomFull, third.Status);
        Assert.Null(third.Handle);
    }

    [Theory]
    [InlineData("ABC")]        // too short
    [InlineData("ABCDE")]      // too long
    [InlineData("AB!Z")]       // punctuation
    [InlineData("ABIZ")]       // excluded letter
    [InlineData("AB0Z")]       // excluded digit
    public void Invalid_codes_are_rejected(string code)
    {
        var manager = NewManager();

        var outcome = manager.JoinOrCreate("conn-1", code);

        Assert.Equal(JoinStatus.InvalidRoomCode, outcome.Status);
        Assert.Null(outcome.Handle);
    }

    [Fact]
    public void An_empty_code_falls_back_to_auto_matching()
    {
        var manager = NewManager();

        foreach (var blank in new[] { null, "", "   " })
        {
            var outcome = manager.JoinOrCreate($"conn-{blank}", blank);
            Assert.Equal(JoinStatus.Joined, outcome.Status);
        }
    }

    [Fact]
    public void Simultaneous_joins_to_the_same_code_do_not_create_two_rooms()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var manager = NewManager();
            var ready = new ManualResetEventSlim(false);
            var outcomes = new JoinOutcome[2];

            var threads = Enumerable.Range(0, 2).Select(i => new Thread(() =>
            {
                ready.Wait();
                outcomes[i] = manager.JoinOrCreate($"conn-{i}", "WXYZ");
            })).ToArray();

            foreach (var thread in threads) thread.Start();
            ready.Set();
            foreach (var thread in threads) thread.Join();

            Assert.All(outcomes, o => Assert.Equal(JoinStatus.Joined, o.Status));
            Assert.Equal("WXYZ", outcomes[0].Handle!.MatchId);
            Assert.Equal("WXYZ", outcomes[1].Handle!.MatchId);
        }
    }

    // ---- lookup ----

    [Fact]
    public void A_room_can_be_found_by_its_code_regardless_of_case()
    {
        var manager = NewManager();
        manager.JoinOrCreate("conn-1", "WXYZ");

        Assert.NotNull(manager.Find("WXYZ"));
        Assert.NotNull(manager.Find("wxyz"));
        Assert.Null(manager.Find("QQQQ"));
    }

    [Fact]
    public void A_connection_can_be_traced_back_to_its_room()
    {
        var manager = NewManager();
        var first = manager.JoinOrCreate("conn-1");
        manager.JoinOrCreate("conn-2");

        Assert.Equal(first.Handle!.MatchId, manager.FindByConnection("conn-1")?.MatchId);
        Assert.Equal(first.Handle.MatchId, manager.FindByConnection("conn-2")?.MatchId);
        Assert.Null(manager.FindByConnection("nobody"));
    }
}
