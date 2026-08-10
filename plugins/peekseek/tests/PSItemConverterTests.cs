using System;
using Newtonsoft.Json;
using PeekSeek.Core.Models;
using Xunit;

namespace PeekSeek.Tests;

public class PSItemConverterTests {
    private class Kit {
        [JsonProperty("kit")]
        [JsonConverter(typeof(PSItemConverter))]
        public System.Collections.Generic.List<PSItem> Items = null!;
    }

    [Fact]
    public void ReadJson_SingleItem_ParsesAllFields() {
        const string json = "{\"kit\":[[363,1,\"100,0,255\"]]}";

        var result = JsonConvert.DeserializeObject<Kit>(json);

        Assert.NotNull(result);
        Assert.Single(result!.Items);
        var item = result.Items[0];
        Assert.Equal((ushort)363, item.ItemID);
        Assert.Equal((byte)1, item.Amount);
        Assert.Equal(new byte[] { 100, 0, 255 }, item.State);
    }

    [Fact]
    public void ReadJson_EmptyStateString_YieldsEmptyByteArray() {
        const string json = "{\"kit\":[[363,1,\"\"]]}";

        var result = JsonConvert.DeserializeObject<Kit>(json);

        Assert.NotNull(result);
        Assert.Single(result!.Items);
        Assert.Empty(result.Items[0].State);
    }

    [Fact]
    public void ReadJson_MultipleItems_ReturnsAllInOrder() {
        const string json = "{\"kit\":[[363,1,\"1,2\"],[519,5,\"\"]]}";

        var result = JsonConvert.DeserializeObject<Kit>(json);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Items.Count);
        Assert.Equal((ushort)363, result.Items[0].ItemID);
        Assert.Equal((ushort)519, result.Items[1].ItemID);
        Assert.Equal((byte)5, result.Items[1].Amount);
    }

    [Fact]
    public void WriteJson_Throws_SoAccidentalSerializationFails() {
        var converter = new PSItemConverter();

        Assert.Throws<NotImplementedException>(
            () => converter.WriteJson(writer: null!, value: null!, serializer: null!)
        );
    }
}
