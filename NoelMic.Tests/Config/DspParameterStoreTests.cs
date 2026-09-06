using NoelMic.Config;

namespace NoelMic.Tests.Config;

public class DspParameterStoreTests
{
    [Fact]
    public void Constructor_SetsCurrentToTheProvidedInitialValue()
    {
        var initial = PresetDefinitions.Get("Raw");
        var store = new DspParameterStore(initial);

        Assert.Same(initial, store.Current);
    }

    [Fact]
    public void Current_AfterUpdate_ReturnsTheMostRecentlyUpdatedValue()
    {
        var store = new DspParameterStore(PresetDefinitions.Get("Raw"));
        var updated = PresetDefinitions.Get("Bright");

        store.Update(updated);

        Assert.Same(updated, store.Current);
    }

    [Fact]
    public void Update_ChangesTheCurrentReference_SoAReferenceEqualsGateCanDetectIt()
    {
        var store = new DspParameterStore(PresetDefinitions.Get("Raw"));
        var before = store.Current;

        store.Update(PresetDefinitions.Get("Bright"));

        Assert.False(ReferenceEquals(before, store.Current));
    }

    [Fact]
    public void Update_WithAStructurallyEquivalentButDistinctInstance_StillChangesTheReference()
    {
        // DspParameters is a record, so structural equality would say these
        // two instances are "equal" -- but DspChain's live-update gate relies
        // on reference equality, not structural equality, so Update must
        // publish the new reference regardless of whether its field values
        // happen to match the previous ones.
        var initial = PresetDefinitions.Get("Raw");
        var store = new DspParameterStore(initial);
        var equivalentCopy = initial with { };

        store.Update(equivalentCopy);

        Assert.Equal(initial, store.Current);
        Assert.False(ReferenceEquals(initial, store.Current));
    }
}
