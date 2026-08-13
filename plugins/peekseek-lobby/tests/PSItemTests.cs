using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The wire shape of one kit item, as `/server/v1/players/{steam64}/kits` sends it.
public class PSItemTests {
    [Fact]
    public void ParseArray_SingleItem_ParsesAllFields() {
        var array = JArray.Parse("[{\"itemId\":363,\"amount\":1,\"state\":[100,0,255]}]");

        var result = PSItem.ParseArray(array);

        Assert.Single(result);
        Assert.Equal((ushort)363, result[0].ItemID);
        Assert.Equal((byte)1, result[0].Amount);
        Assert.Equal(new byte[] { 100, 0, 255 }, result[0].State);
    }

    [Fact]
    public void ParseArray_EmptyState_YieldsEmptyByteArray() {
        var array = JArray.Parse("[{\"itemId\":363,\"amount\":1,\"state\":[]}]");

        var result = PSItem.ParseArray(array);

        Assert.Empty(result[0].State);
    }

    /// An item with no state at all is still an item — the site omits the field for
    /// anything that carries no attachment bytes, and an outfit piece that fails to
    /// parse is an outfit piece the player spawns without.
    [Fact]
    public void ParseArray_MissingState_YieldsEmptyByteArray() {
        var array = JArray.Parse("[{\"itemId\":363,\"amount\":1}]");

        var result = PSItem.ParseArray(array);

        Assert.Empty(result[0].State);
    }

    [Fact]
    public void ParseArray_MultipleItems_ReturnsAllInOrder() {
        var array = JArray.Parse(
            "[{\"itemId\":363,\"amount\":1,\"state\":[1,2]},{\"itemId\":519,\"amount\":5,\"state\":[]}]");

        var result = PSItem.ParseArray(array);

        Assert.Equal(2, result.Count);
        Assert.Equal((ushort)363, result[0].ItemID);
        Assert.Equal((ushort)519, result[1].ItemID);
        Assert.Equal((byte)5, result[1].Amount);
    }

    [Fact]
    public void Parse_SingleEntry_ParsesAllFields() {
        var entry = JObject.Parse("{\"itemId\":6,\"amount\":5,\"state\":[]}");

        var result = PSItem.Parse(entry);

        Assert.Equal((ushort)6, result.ItemID);
        Assert.Equal((byte)5, result.Amount);
        Assert.Empty(result.State);
    }
}
