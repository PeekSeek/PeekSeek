using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace PeekSeekRaid.Core.Models;

/// Pieces that one budget count can stand as: a maple wall that can be a rampart or a
/// window, a maple roof that can be a hole. `Counts` is the id the budget knows, and
/// `Forms` is everything it can be turned into once it stands, itself included.
///
/// Turning a piece into another form of its own family is free, because it is still
/// the one wall the team paid for. Taking it down gives back `Counts`, whichever form
/// it is in at the time.
public class PieceFamily {
    [XmlAttribute]
    public ushort Counts;

    [XmlElement("Form")]
    public List<ushort> Forms = [];

    public PieceFamily() { }

    public PieceFamily(ushort counts, params ushort[] forms) {
        Counts = counts;
        Forms = forms.ToList();
    }

    /// The family a standing piece belongs to, or null for a piece that is only itself.
    public static PieceFamily Of(IEnumerable<PieceFamily> families, ushort id) {
        return families.FirstOrDefault(family => family.Forms.Contains(id));
    }

    /// What taking a piece down gives back: its family's count, or the piece itself.
    public static ushort RefundFor(IEnumerable<PieceFamily> families, ushort id) {
        return Of(families, id)?.Counts ?? id;
    }
}
